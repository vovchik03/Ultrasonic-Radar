/**
  ******************************************************************************
  * @file           : ultrasonic.h
  * @brief          : HC-SR04 ultrasonic sensor driver.
  *                   TRIG -> PB7 (GPIO output)
  *                   ECHO -> PB6 (TIM4 CH1/CH2 input capture, 5V tolerant pin)
  *                   Measurements run automatically every ULTRASONIC_PERIOD_MS,
  *                   the echo pulse is timed by hardware with 1 us resolution.
  ******************************************************************************
  */
#ifndef __ULTRASONIC_H
#define __ULTRASONIC_H

#ifdef __cplusplus
extern "C" {
#endif

#include <stdint.h>

/* Minimum time between two pings. HC-SR04 datasheet recommends >= 60 ms,
   otherwise the echo of the previous ping can be taken as the new one. */
#define ULTRASONIC_PERIOD_MS          60U

/* No valid echo within this time -> ULTRASONIC_NO_ECHO.
   HC-SR04 holds ECHO high for ~38 ms when nothing is in range. */
#define ULTRASONIC_TIMEOUT_MS         50U

/* Results farther than this are reported as ULTRASONIC_OUT_OF_RANGE */
#define ULTRASONIC_MAX_DISTANCE_MM    4000U

/* Speed of sound, m/s (343 at 20 C) */
#define ULTRASONIC_SOUND_SPEED_MPS    343U

typedef enum
{
  ULTRASONIC_OK = 0,        /* distance is valid                        */
  ULTRASONIC_OUT_OF_RANGE,  /* echo received, but farther than the max  */
  ULTRASONIC_NO_ECHO,       /* sensor did not answer (wiring / timeout) */
  ULTRASONIC_NOT_READY      /* no measurement finished yet              */
} Ultrasonic_Status;

void              Ultrasonic_Init(void);

/* Call from the main loop as often as possible (non-blocking).
   Starts a new ping every ULTRASONIC_PERIOD_MS and collects the result.
   Returns 1 when a new result is available, 0 otherwise. */
uint8_t           Ultrasonic_Update(void);

Ultrasonic_Status Ultrasonic_GetStatus(void);
uint16_t          Ultrasonic_GetDistanceMm(void);  /* 0 if status != OK */
uint16_t          Ultrasonic_GetEchoUs(void);      /* raw echo pulse width */

#ifdef __cplusplus
}
#endif

#endif /* __ULTRASONIC_H */
