/**
  ******************************************************************************
  * @file           : scan.c
  * @brief          : Radar sector scanner (servo + HC-SR04).
  *
  *  State machine, one measured point per cycle:
  *
  *    MOVE    : servo commanded to the next angle, wait the settle time and
  *              until the sensor is free, then request one ping
  *    MEASURE : wait for the echo result, store it, go to MOVE
  *
  *  The mode picks the next angle after every point:
  *
  *    SWEEP   : back and forth inside [from..to]           (SCAN)
  *    GOTO    : one way from the start angle to the target
  *              on the same grid, then HOLD                (GOTO)
  *    HOLD    : the same angle again and again             (HOLD)
  *
  *  With a speed limit (Scan_SetSpeed) MOVE also steps the servo degree by
  *  degree towards the next angle instead of commanding it at once.
  *
  *  The grid step is set by Scan_SetStep (9 deg after power-up).
  *
  *  The ultrasonic driver runs in single-shot mode, so a result always
  *  belongs to the ping requested at the current angle. Without a speed
  *  limit one 9 deg step takes 70..108 ms (servo settle + echo, at least
  *  ULTRASONIC_PERIOD_MS), a 0..180 sweep takes 1.4..2.2 s. Speed effect
  *  and the recommended range: see SCAN_SPEED_MAX_DPS in scan.h.
  ******************************************************************************
  */
#include "scan.h"
#include "servo.h"
#include "ultrasonic.h"
#include "main.h"

typedef enum
{
  SCAN_STATE_STOPPED = 0,
  SCAN_STATE_MOVE,
  SCAN_STATE_MEASURE
} Scan_State;

typedef enum
{
  SCAN_MODE_SWEEP = 0,
  SCAN_MODE_GOTO,
  SCAN_MODE_HOLD
} Scan_Mode;

static Scan_State scan_state;
static Scan_Mode  scan_mode;
static uint8_t    scan_from;
static uint8_t    scan_to;
static uint8_t    scan_angle;        /* angle of the point being measured */
static uint8_t    scan_last_angle;
static int8_t     scan_dir;
static uint8_t    scan_target;       /* GOTO end angle */
static uint32_t   scan_move_tick;
static uint32_t   scan_settle_ms;
static uint8_t    scan_move_from;    /* servo angle when the move started */
static uint32_t   scan_ramp_ms;      /* speed-limited move time, 0 = no limit */
static uint16_t   scan_speed_dps;    /* 0 = no limit */
static uint8_t    scan_step;         /* grid step, 1..SCAN_STEP_MAX_DEG */
static uint16_t   scan_map[SCAN_ANGLE_MAX + 1U];

static void Scan_MoveTo(uint8_t angle)
{
  scan_move_from = (uint8_t)Servo_GetAngle();

  int16_t travel = (int16_t)angle - scan_move_from;
  if (travel < 0)
  {
    travel = -travel;
  }

  /* Time the servo needs at its own top speed */
  uint32_t travel_ms = (uint32_t)travel * SCAN_SETTLE_PER_DEG_MS;

  if (scan_speed_dps == 0U)
  {
    scan_ramp_ms = 0;
    Servo_SetAngle(angle);
  }
  else
  {
    /* Scan_Ramp() walks the servo there, the settle time starts at its end */
    scan_ramp_ms = ((uint32_t)travel * 1000U) / scan_speed_dps;
    if (scan_ramp_ms > travel_ms)
    {
      travel_ms = scan_ramp_ms;
    }
  }

  scan_angle = angle;
  scan_settle_ms = SCAN_SETTLE_BASE_MS + travel_ms;
  scan_move_tick = HAL_GetTick();
  scan_state = SCAN_STATE_MOVE;
}

/* Speed-limited move: servo position for the time since Scan_MoveTo() */
static void Scan_Ramp(void)
{
  int16_t  pos = (int16_t)scan_angle;
  uint32_t elapsed = HAL_GetTick() - scan_move_tick;

  if (elapsed < scan_ramp_ms)
  {
    /* elapsed * speed < travel * 1000, no overflow */
    int16_t moved = (int16_t)((elapsed * scan_speed_dps) / 1000U);
    pos = (scan_angle > scan_move_from) ? (int16_t)(scan_move_from + moved)
                                        : (int16_t)(scan_move_from - moved);
  }

  if (pos != Servo_GetAngle())
  {
    Servo_SetAngle(pos);
  }
}

/* Grid point above the angle, the last step is shortened to land on "to" */
static uint8_t Scan_GridUp(uint8_t angle)
{
  uint16_t next = (uint16_t)angle + scan_step;
  return (next > scan_to) ? scan_to : (uint8_t)next;
}

/* Grid point below the angle (from, from + step, ...), angle > from */
static uint8_t Scan_GridDown(uint8_t angle)
{
  return (uint8_t)(scan_from + ((angle - scan_from - 1U) / scan_step) * scan_step);
}

static uint8_t Scan_NextAngle(void)
{
  switch (scan_mode)
  {
    case SCAN_MODE_HOLD:
      return scan_angle;

    case SCAN_MODE_GOTO:
      if (scan_angle == scan_target)
      {
        scan_mode = SCAN_MODE_HOLD;    /* arrived */
        return scan_angle;
      }
      /* The target is a sector edge, so the grid steps end exactly on it */
      return (scan_dir > 0) ? Scan_GridUp(scan_angle) : Scan_GridDown(scan_angle);

    default:
      break;
  }

  /* SWEEP over a single-angle sector: keep measuring the same angle */
  if (scan_from == scan_to)
  {
    return scan_from;
  }

  /* Bounce off the sector edges */
  if (scan_angle >= scan_to)
  {
    scan_dir = -1;
  }
  else if (scan_angle <= scan_from)
  {
    scan_dir = 1;
  }

  return (scan_dir > 0) ? Scan_GridUp(scan_angle) : Scan_GridDown(scan_angle);
}

/* Sets the sector (order does not matter, clamped to 0..180)
   and clears all stored distances */
static void Scan_SetSector(uint8_t from, uint8_t to)
{
  if (from > to)
  {
    uint8_t tmp = from;
    from = to;
    to = tmp;
  }
  if (to > SCAN_ANGLE_MAX)
  {
    to = SCAN_ANGLE_MAX;
  }
  if (from > to)
  {
    from = to;
  }

  scan_from = from;
  scan_to = to;

  for (uint16_t i = 0; i <= SCAN_ANGLE_MAX; i++)
  {
    scan_map[i] = 0;
  }
}

static uint8_t Scan_ClampAngle(uint8_t angle)
{
  return (angle > SCAN_ANGLE_MAX) ? (uint8_t)SCAN_ANGLE_MAX : angle;
}

void Scan_Init(void)
{
  /* Pings are requested by the scanner, one per angle */
  Ultrasonic_SetAutoMode(0);
  scan_last_angle = (uint8_t)Servo_GetAngle();
  scan_angle = scan_last_angle;
  scan_speed_dps = SCAN_SPEED_DEFAULT_DPS;
  scan_step = SCAN_STEP_DEFAULT_DEG;

  /* Idle after power-up: the servo holds its angle, no pings
     until SCAN / GOTO / HOLD */
  Scan_SetSector(SCAN_DEFAULT_FROM, SCAN_DEFAULT_TO);
  scan_mode = SCAN_MODE_SWEEP;
  scan_state = SCAN_STATE_STOPPED;
}

void Scan_Start(uint8_t from, uint8_t to)
{
  Scan_SetSector(from, to);
  scan_mode = SCAN_MODE_SWEEP;

  /* Start from the sector edge closest to the current servo position */
  int16_t pos = Servo_GetAngle();
  if ((pos - (int16_t)scan_from) <= ((int16_t)scan_to - pos))
  {
    scan_dir = 1;
    Scan_MoveTo(scan_from);
  }
  else
  {
    scan_dir = -1;
    Scan_MoveTo(scan_to);
  }
}

void Scan_GoTo(uint8_t angle_deg)
{
  uint8_t pos = (uint8_t)Servo_GetAngle();

  scan_target = Scan_ClampAngle(angle_deg);
  Scan_SetSector(pos, scan_target);
  scan_mode = SCAN_MODE_GOTO;
  scan_dir = (scan_target >= pos) ? 1 : -1;

  /* First point right where the servo is now */
  Scan_MoveTo(pos);
}

void Scan_Hold(uint8_t angle_deg)
{
  angle_deg = Scan_ClampAngle(angle_deg);
  Scan_SetSector(angle_deg, angle_deg);
  scan_mode = SCAN_MODE_HOLD;
  Scan_MoveTo(angle_deg);
}

void Scan_Stop(void)
{
  scan_state = SCAN_STATE_STOPPED;
}

void Scan_SetSpeed(uint16_t deg_per_s)
{
  scan_speed_dps = (deg_per_s > SCAN_SPEED_MAX_DPS) ? (uint16_t)SCAN_SPEED_MAX_DPS : deg_per_s;
}

uint16_t Scan_GetSpeed(void)
{
  return scan_speed_dps;
}

void Scan_SetStep(uint8_t step_deg)
{
  if (step_deg == 0U)
  {
    step_deg = 1U;
  }
  scan_step = (step_deg > SCAN_STEP_MAX_DEG) ? (uint8_t)SCAN_STEP_MAX_DEG : step_deg;
}

uint8_t Scan_GetStep(void)
{
  return scan_step;
}

uint8_t Scan_IsRunning(void)
{
  return (scan_state != SCAN_STATE_STOPPED) ? 1U : 0U;
}

uint8_t Scan_GetFrom(void)
{
  return scan_from;
}

uint8_t Scan_GetTo(void)
{
  return scan_to;
}

uint16_t Scan_GetDistanceMm(uint8_t angle_deg)
{
  if (angle_deg > SCAN_ANGLE_MAX)
  {
    return 0;
  }
  return scan_map[angle_deg];
}

uint8_t Scan_GetLastAngle(void)
{
  return scan_last_angle;
}

uint8_t Scan_Update(void)
{
  uint8_t result = Ultrasonic_Update();

  switch (scan_state)
  {
    case SCAN_STATE_MOVE:
      Scan_Ramp();

      /* A result arriving here is from a ping requested before the move
         (e.g. STOP + SCAN while measuring) and is ignored */
      if (((HAL_GetTick() - scan_move_tick) >= scan_settle_ms) && !Ultrasonic_IsBusy())
      {
        Ultrasonic_Trigger();
        scan_state = SCAN_STATE_MEASURE;
      }
      break;

    case SCAN_STATE_MEASURE:
      if (result)
      {
        scan_map[scan_angle] = (Ultrasonic_GetStatus() == ULTRASONIC_OK) ?
                               Ultrasonic_GetDistanceMm() : 0U;
        scan_last_angle = scan_angle;
        Scan_MoveTo(Scan_NextAngle());
        return 1;
      }
      break;

    default:
      break;
  }

  return 0;
}
