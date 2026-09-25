/**
  ******************************************************************************
  * @file           : servo.h
  * @brief          : Hobby servo driver (TIM2 CH1 PWM on PA0, 50 Hz).
  *                   Sweeps back and forth in one of three ranges:
  *                   180, 90 or 45 degrees. The narrower the range,
  *                   the faster the servo moves.
  ******************************************************************************
  */
#ifndef __SERVO_H
#define __SERVO_H

#ifdef __cplusplus
extern "C" {
#endif

#include <stdint.h>

/* Pulse width limits of the servo, adjust for your model (SG90/MG90S: ~500..2500 us) */
#define SERVO_PULSE_MIN_US   500U    /* pulse at 0 deg   */
#define SERVO_PULSE_MAX_US   2500U   /* pulse at 180 deg */

/* Sweep ranges are centered around this angle */
#define SERVO_CENTER_DEG     90

typedef enum
{
  SERVO_MODE_180 = 0,   /* 0..180 deg, slow   */
  SERVO_MODE_90,        /* 45..135 deg, medium */
  SERVO_MODE_45,        /* 68..113 deg, fast   */
  SERVO_MODE_COUNT
} Servo_Mode;

void       Servo_Init(Servo_Mode mode);
void       Servo_SetMode(Servo_Mode mode);
Servo_Mode Servo_GetMode(void);
void       Servo_NextMode(void);
void       Servo_SetAngle(int16_t angle_deg);
int16_t    Servo_GetAngle(void);

/* Call from the main loop as often as possible (non-blocking).
   Returns 1 when the servo moved one step, 0 otherwise. */
uint8_t    Servo_Update(void);

#ifdef __cplusplus
}
#endif

#endif /* __SERVO_H */
