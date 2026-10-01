/**
  ******************************************************************************
  * @file           : ultrasonic.c
  * @brief          : HC-SR04 ultrasonic sensor driver.
  *
  *  TIM4 runs freely at 1 MHz. Both capture channels listen to the same
  *  ECHO pin (TI1): CH1 latches the rising edge, CH2 the falling edge, so
  *  the pulse width is measured in hardware and the main loop only has to
  *  poll the flags - no interrupts, no dependency on main loop timing.
  *
  *  TIM4 is configured directly through registers, so the driver does not
  *  depend on HAL_TIM_MODULE_ENABLED and survives CubeMX code regeneration.
  *  If TIM4 / PB6 / PB7 are later configured in CubeMX, remove them there or here.
  ******************************************************************************
  */
#include "ultrasonic.h"
#include "main.h"

/* TRIG / ECHO pins are defined in main.h (US_TRIG_*, US_ECHO_*) */

#define US_TRIG_PULSE_US     12U            /* datasheet: >= 10 us */

typedef enum
{
  US_STATE_IDLE = 0,
  US_STATE_WAIT_ECHO
} Ultrasonic_State;

static Ultrasonic_State  us_state;
static Ultrasonic_Status us_status;
static uint16_t          us_distance_mm;
static uint16_t          us_echo_us;
static uint32_t          us_ping_tick;

static void Ultrasonic_HwInit(void)
{
  GPIO_InitTypeDef GPIO_InitStruct = {0};

  __HAL_RCC_GPIOB_CLK_ENABLE();
  __HAL_RCC_TIM4_CLK_ENABLE();

  /* PB7 -> TRIG, push-pull output, idle low */
  HAL_GPIO_WritePin(US_TRIG_GPIO_Port, US_TRIG_Pin, GPIO_PIN_RESET);
  GPIO_InitStruct.Pin = US_TRIG_Pin;
  GPIO_InitStruct.Mode = GPIO_MODE_OUTPUT_PP;
  GPIO_InitStruct.Pull = GPIO_NOPULL;
  GPIO_InitStruct.Speed = GPIO_SPEED_FREQ_LOW;
  HAL_GPIO_Init(US_TRIG_GPIO_Port, &GPIO_InitStruct);

  /* PB6 -> ECHO, input (pull-down keeps it low if the sensor is unplugged) */
  GPIO_InitStruct.Pin = US_ECHO_Pin;
  GPIO_InitStruct.Mode = GPIO_MODE_INPUT;
  GPIO_InitStruct.Pull = GPIO_PULLDOWN;
  HAL_GPIO_Init(US_ECHO_GPIO_Port, &GPIO_InitStruct);

  /* APB1 timer clock is doubled when the APB1 prescaler is not 1 */
  uint32_t tim_clk = HAL_RCC_GetPCLK1Freq();
  if ((RCC->CFGR & RCC_CFGR_PPRE1) != RCC_CFGR_PPRE1_DIV1)
  {
    tim_clk *= 2U;
  }

  TIM4->CR1   = 0;
  TIM4->PSC   = (tim_clk / 1000000U) - 1U;    /* 1 tick = 1 us            */
  TIM4->ARR   = 0xFFFFU;                      /* 65.5 ms wrap > 38 ms echo */

  /* CH1: IC1 <- TI1, CH2: IC2 <- TI1, digital filter fCK_INT N=8 on both */
  TIM4->CCMR1 = TIM_CCMR1_CC1S_0 | TIM_CCMR1_IC1F_1 | TIM_CCMR1_IC1F_0 |
                TIM_CCMR1_CC2S_1 | TIM_CCMR1_IC2F_1 | TIM_CCMR1_IC2F_0;
  /* CH1 captures rising edge, CH2 captures falling edge */
  TIM4->CCER  = TIM_CCER_CC1E | TIM_CCER_CC2E | TIM_CCER_CC2P;

  TIM4->EGR   = TIM_EGR_UG;                   /* load PSC */
  TIM4->SR    = 0;
  TIM4->CR1   = TIM_CR1_CEN;
}

static void Ultrasonic_StartPing(void)
{
  /* Drop captures left over from the previous cycle */
  TIM4->SR = 0;

  HAL_GPIO_WritePin(US_TRIG_GPIO_Port, US_TRIG_Pin, GPIO_PIN_SET);
  uint16_t start = (uint16_t)TIM4->CNT;
  while ((uint16_t)((uint16_t)TIM4->CNT - start) < US_TRIG_PULSE_US)
  {
  }
  HAL_GPIO_WritePin(US_TRIG_GPIO_Port, US_TRIG_Pin, GPIO_PIN_RESET);

  us_ping_tick = HAL_GetTick();
  us_state = US_STATE_WAIT_ECHO;
}

static void Ultrasonic_SetResult(Ultrasonic_Status status, uint16_t echo_us)
{
  /* us * m/s = um; / 1000 -> mm; / 2 -> sound travels there and back */
  uint32_t distance = ((uint32_t)echo_us * ULTRASONIC_SOUND_SPEED_MPS) / 2000U;

  if ((status == ULTRASONIC_OK) && (distance > ULTRASONIC_MAX_DISTANCE_MM))
  {
    status = ULTRASONIC_OUT_OF_RANGE;
  }

  us_status = status;
  us_echo_us = echo_us;
  us_distance_mm = (status == ULTRASONIC_OK) ? (uint16_t)distance : 0U;
  us_state = US_STATE_IDLE;
}

void Ultrasonic_Init(void)
{
  us_state = US_STATE_IDLE;
  us_status = ULTRASONIC_NOT_READY;
  us_distance_mm = 0;
  us_echo_us = 0;
  Ultrasonic_HwInit();
  /* First ping is sent on the first Ultrasonic_Update() call */
  us_ping_tick = HAL_GetTick() - ULTRASONIC_PERIOD_MS;
}

uint8_t Ultrasonic_Update(void)
{
  uint32_t now = HAL_GetTick();

  if (us_state == US_STATE_IDLE)
  {
    if ((now - us_ping_tick) >= ULTRASONIC_PERIOD_MS)
    {
      Ultrasonic_StartPing();
    }
    return 0;
  }

  /* Falling edge captured -> echo pulse is complete */
  if (TIM4->SR & TIM_SR_CC2IF)
  {
    if (TIM4->SR & TIM_SR_CC1IF)
    {
      uint16_t rise = (uint16_t)TIM4->CCR1;   /* reading CCRx clears CCxIF */
      uint16_t fall = (uint16_t)TIM4->CCR2;
      Ultrasonic_SetResult(ULTRASONIC_OK, (uint16_t)(fall - rise));
    }
    else
    {
      /* Falling edge without a rising one: ECHO was already high
         before the ping (stale echo), the result is meaningless */
      (void)TIM4->CCR2;
      Ultrasonic_SetResult(ULTRASONIC_NO_ECHO, 0);
    }
    return 1;
  }

  if ((now - us_ping_tick) >= ULTRASONIC_TIMEOUT_MS)
  {
    Ultrasonic_SetResult(ULTRASONIC_NO_ECHO, 0);
    return 1;
  }

  return 0;
}

Ultrasonic_Status Ultrasonic_GetStatus(void)
{
  return us_status;
}

uint16_t Ultrasonic_GetDistanceMm(void)
{
  return us_distance_mm;
}

uint16_t Ultrasonic_GetEchoUs(void)
{
  return us_echo_us;
}
