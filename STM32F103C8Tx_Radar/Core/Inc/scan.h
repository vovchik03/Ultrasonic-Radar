/**
  ******************************************************************************
  * @file           : scan.h
  * @brief          : Radar sector scanner (servo + HC-SR04).
  *                   Step-and-measure: move the servo one step, wait until it
  *                   settles, ping, store the distance for that angle, next
  *                   step. The servo sweeps back and forth inside the sector
  *                   [from..to], so every distance belongs to a known angle.
  ******************************************************************************
  */
#ifndef __SCAN_H
#define __SCAN_H

#ifdef __cplusplus
extern "C" {
#endif

#include <stdint.h>

#define SCAN_ANGLE_MAX           180U

/* Angle between two neighbouring measurements */
#define SCAN_STEP_DEG            1U

/* Servo settle time before a ping: BASE + PER_DEG * travelled degrees.
   BASE covers the 20 ms PWM period (new pulse width is applied on the next
   period) and the mechanical wobble of the sensor after a stop. */
#define SCAN_SETTLE_BASE_MS      40U
#define SCAN_SETTLE_PER_DEG_MS   2U     /* SG90: ~0.1 s / 60 deg */

/* Sector scanned after power-up */
#define SCAN_DEFAULT_FROM        0U
#define SCAN_DEFAULT_TO          180U

void     Scan_Init(void);

/* Start sweeping [from..to] deg (0..180, order does not matter).
   Clears all stored distances. */
void     Scan_Start(uint8_t from, uint8_t to);

/* Stop sweeping, the servo holds its current angle */
void     Scan_Stop(void);

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
