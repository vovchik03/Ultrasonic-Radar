/**
  ******************************************************************************
  * @file           : scan.h
  * @brief          : Radar sector scanner (servo + HC-SR04).
  *                   Step-and-measure: move the servo one step, wait until it
  *                   settles, ping, store the distance for that angle, next
  *                   step. The servo sweeps back and forth inside the sector
  *                   [from..to] (or goes to / holds one angle, see
  *                   Scan_GoTo / Scan_Hold), so every distance belongs to a
  *                   known angle.
  ******************************************************************************
  */
#ifndef __SCAN_H
#define __SCAN_H

#ifdef __cplusplus
extern "C" {
#endif

#include <stdint.h>

#define SCAN_ANGLE_MAX           180U

/* Angle between two neighbouring measurements, set by Scan_SetStep (STEP
   command). HC-SR04 beam is ~15 deg wide, so up to ~15 deg neighbouring
   beams still overlap. Points lie on the grid from, from + step, ... plus
   the sector edge "to" itself. */
#define SCAN_STEP_DEFAULT_DEG    9U
#define SCAN_STEP_MAX_DEG        30U

/* Servo settle time before a ping: BASE + PER_DEG * travelled degrees.
   BASE covers the 20 ms PWM period (new pulse width is applied on the next
   period) and the mechanical wobble of the sensor after a stop. */
#define SCAN_SETTLE_BASE_MS      40U
#define SCAN_SETTLE_PER_DEG_MS   2U     /* SG90: ~0.1 s / 60 deg */

/* Servo speed limit between two points, deg/s. 0 = no limit: the servo gets
   the new angle at once and turns at its own top speed (1000 /
   SCAN_SETTLE_PER_DEG_MS = 500 deg/s), so SCAN_SPEED_MAX_DPS acts the same.
 *
 * Time per point (one ping):
 *   T      = max(ULTRASONIC_PERIOD_MS, settle + t_echo)
 *   settle = SCAN_SETTLE_BASE_MS + max(2 ms * step, 1000 * step / speed)
 *   t_echo ~ 0.5 ms + 5.83 ms per metre (sound there and back, 343 m/s),
 *            38..50 ms when nothing is in range
 *
 * 9 deg step, object at 1 m (t_echo ~ 6 ms), 0..180 sweep = 20 steps:
 *   speed 0 / 500 :  70 ms per point, 14.3 Hz, sweep 1.4 s
 *   speed 180     :  96 ms,           10.4 Hz, sweep 1.9 s
 *   speed 90      : 146 ms,            6.8 Hz, sweep 2.9 s
 *   speed 45      : 246 ms,            4.1 Hz, sweep 4.9 s
 *   speed 20      : 496 ms,            2.0 Hz, sweep 9.9 s
 * Above ~380 deg/s the move hides inside the 70 ms ping period, so the rate
 * does not change any more.
 *
 * Recommended 90..180 deg/s: smooth motion, much lower servo current peaks
 * than the full-speed jump (less 5 V sag, fewer false echoes), and a sweep
 * still under 3 s. Below ~45 deg/s the picture updates too slowly (> 5 s
 * per sweep). 0 = fastest scan, but the servo jerks every step. */
#define SCAN_SPEED_MAX_DPS       500U
#define SCAN_SPEED_DEFAULT_DPS   0U

/* Sector reported by GET after power-up (all 0, nothing measured yet) */
#define SCAN_DEFAULT_FROM        0U
#define SCAN_DEFAULT_TO          180U

/* Stopped after init: the servo holds its angle and waits for
   Scan_Start / Scan_GoTo / Scan_Hold */
void     Scan_Init(void);

/* Start sweeping [from..to] deg (0..180, order does not matter).
   Clears all stored distances. */
void     Scan_Start(uint8_t from, uint8_t to);

/* Move from the current servo angle to angle_deg (0..180) step by step,
   measuring on the way like a one-way sweep, then hold there and keep
   measuring that angle. The sector becomes [current..angle_deg].
   Clears all stored distances. */
void     Scan_GoTo(uint8_t angle_deg);

/* Turn straight to angle_deg (0..180) without measuring on the way, then
   keep measuring that angle. The sector becomes [angle_deg..angle_deg].
   Clears all stored distances. */
void     Scan_Hold(uint8_t angle_deg);

/* Stop sweeping, the servo holds its current angle */
void     Scan_Stop(void);

/* Servo speed for all moves, deg/s: 0 = no limit, 1..SCAN_SPEED_MAX_DPS
   (larger values are clamped). Applies from the next move. */
void     Scan_SetSpeed(uint16_t deg_per_s);
uint16_t Scan_GetSpeed(void);

/* Grid step for SCAN and GOTO, 1..SCAN_STEP_MAX_DEG (clamped).
   Applies from the next move. */
void     Scan_SetStep(uint8_t step_deg);
uint8_t  Scan_GetStep(void);

uint8_t  Scan_IsRunning(void);
uint8_t  Scan_GetFrom(void);
uint8_t  Scan_GetTo(void);

/* Last measured distance at this angle, 0 = no echo / not measured yet */
uint16_t Scan_GetDistanceMm(uint8_t angle_deg);

/* Angle of the most recent measurement */
uint8_t  Scan_GetLastAngle(void);

/* Call from the main loop as often as possible (non-blocking).
   Also drives the ultrasonic driver, do not call Ultrasonic_Update() elsewhere.
   Returns 1 when a new point was measured, 0 otherwise. */
uint8_t  Scan_Update(void);

#ifdef __cplusplus
}
#endif

#endif /* __SCAN_H */
