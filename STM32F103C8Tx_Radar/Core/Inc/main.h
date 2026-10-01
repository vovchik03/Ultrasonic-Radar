/* USER CODE BEGIN Header */
/**
  ******************************************************************************
  * @file           : main.h
  * @brief          : Header for main.c file.
  *                   This file contains the common defines of the application.
  ******************************************************************************
  * @attention
  *
  * Copyright (c) 2026 STMicroelectronics.
  * All rights reserved.
  *
  * This software is licensed under terms that can be found in the LICENSE file
  * in the root directory of this software component.
  * If no LICENSE file comes with this software, it is provided AS-IS.
  *
  ******************************************************************************
  */
/* USER CODE END Header */

/* Define to prevent recursive inclusion -------------------------------------*/
#ifndef __MAIN_H
#define __MAIN_H

#ifdef __cplusplus
extern "C" {
#endif

/* Includes ------------------------------------------------------------------*/
#include "stm32f1xx_hal.h"

/* Private includes ----------------------------------------------------------*/
/* USER CODE BEGIN Includes */

/* USER CODE END Includes */

/* Exported types ------------------------------------------------------------*/
/* USER CODE BEGIN ET */

/* USER CODE END ET */

/* Exported constants --------------------------------------------------------*/
/* USER CODE BEGIN EC */

/* USER CODE END EC */

/* Exported macro ------------------------------------------------------------*/
/* USER CODE BEGIN EM */

/* USER CODE END EM */

/* Exported functions prototypes ---------------------------------------------*/
void Error_Handler(void);

/* USER CODE BEGIN EFP */

/* USER CODE END EFP */

/* Private defines -----------------------------------------------------------*/
#define LED_Pin 				GPIO_PIN_13
#define LED_GPIO_Port 			GPIOC

/* USER CODE BEGIN Private defines */
/* ============================================================================
 * Pin map (STM32F103C8T6, Blue Pill)
 *
 *  Pin   Function           Peripheral   Note
 *  ----  -----------------  -----------  ------------------------------------
 *  PC13  LED                GPIO out     on-board LED, active low (CubeMX label)
 *  PA0   SERVO_PWM          TIM2_CH1     50 Hz PWM, servo signal wire
 *  PB6   US_ECHO            TIM4_CH1     HC-SR04 ECHO, 5 V tolerant (FT) pin
 *  PB7   US_TRIG            GPIO out     HC-SR04 TRIG, >= 10 us high pulse
 *
 *  Reserved, do not use as GPIO:
 *  PA13  SWDIO              SYS          ST-Link debug
 *  PA14  SWCLK              SYS          ST-Link debug
 *  PD0   OSC_IN             RCC          8 MHz HSE crystal
 *  PD1   OSC_OUT            RCC          8 MHz HSE crystal
 *  PA11  USB_DM             USB          planned USB link to the PC app
 *  PA12  USB_DP             USB          planned USB link to the PC app
 *
 *  Pins configured in CubeMX get their defines above (generated from the
 *  GPIO User Label). Pins below are set up by the drivers through registers,
 *  bypassing CubeMX. If one of them is later configured in CubeMX with a
 *  label, remove it from here.
 * ==========================================================================*/

/* Servo (servo.c) */
#define SERVO_PWM_Pin           GPIO_PIN_0    /* PA0, TIM2_CH1, alternate function push-pull */
#define SERVO_PWM_GPIO_Port     GPIOA

/* HC-SR04 ultrasonic sensor (ultrasonic.c) */
#define US_TRIG_Pin             GPIO_PIN_7    /* PB7, push-pull output, idle low */
#define US_TRIG_GPIO_Port       GPIOB
#define US_ECHO_Pin             GPIO_PIN_6    /* PB6, TIM4_CH1 input capture, pull-down */
#define US_ECHO_GPIO_Port       GPIOB
/* USER CODE END Private defines */

#ifdef __cplusplus
}
#endif

#endif /* __MAIN_H */
