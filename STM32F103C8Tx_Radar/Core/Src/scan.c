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
  *  The ultrasonic driver runs in single-shot mode, so a result always
  *  belongs to the ping requested at the current angle. One 9 deg step
  *  takes 64..108 ms (servo settle + echo), a 0..180 sweep takes 1.3..2.2 s.
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

static Scan_State scan_state;
static uint8_t    scan_from;
static uint8_t    scan_to;
static uint8_t    scan_angle;        /* angle of the point being measured */
static uint8_t    scan_last_angle;
static int8_t     scan_dir;
static uint32_t   scan_move_tick;
static uint32_t   scan_settle_ms;
static uint16_t   scan_map[SCAN_ANGLE_MAX + 1U];

static void Scan_MoveTo(uint8_t angle)
{
  int16_t travel = (int16_t)angle - Servo_GetAngle();
  if (travel < 0)
  {
    travel = -travel;
  }

  Servo_SetAngle(angle);
  scan_angle = angle;
  scan_settle_ms = SCAN_SETTLE_BASE_MS + (uint32_t)travel * SCAN_SETTLE_PER_DEG_MS;
  scan_move_tick = HAL_GetTick();
  scan_state = SCAN_STATE_MOVE;
}

/* Grid point above the angle, the last step is shortened to land on "to" */
static uint8_t Scan_GridUp(uint8_t angle)
{
  uint16_t next = (uint16_t)angle + SCAN_STEP_DEG;
  return (next > scan_to) ? scan_to : (uint8_t)next;
}

/* Grid point below the angle (from, from + STEP, ...), angle > from */
static uint8_t Scan_GridDown(uint8_t angle)
{
  return (uint8_t)(scan_from + ((angle - scan_from - 1U) / SCAN_STEP_DEG) * SCAN_STEP_DEG);
}

static uint8_t Scan_NextAngle(void)
{
  /* Single-angle sector: keep measuring the same angle */
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

void Scan_Init(void)
{
  /* Pings are requested by the scanner, one per angle */
  Ultrasonic_SetAutoMode(0);
  scan_last_angle = (uint8_t)Servo_GetAngle();
  Scan_Start(SCAN_DEFAULT_FROM, SCAN_DEFAULT_TO);
}

void Scan_Start(uint8_t from, uint8_t to)
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

  /* Start from the sector edge closest to the current servo position */
  int16_t pos = Servo_GetAngle();
  if ((pos - (int16_t)from) <= ((int16_t)to - pos))
  {
    scan_dir = 1;
    Scan_MoveTo(from);
  }
  else
  {
    scan_dir = -1;
    Scan_MoveTo(to);
  }
}

void Scan_Stop(void)
{
  scan_state = SCAN_STATE_STOPPED;
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
