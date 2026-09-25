/**
  ******************************************************************************
  * @file           : servo.c
  * @brief          : Hobby servo driver (TIM2 CH1 PWM on PA0, 50 Hz).
  *
  *  TIM2 is configured directly through registers, so the driver does not
  *  depend on HAL_TIM_MODULE_ENABLED and survives CubeMX code regeneration.
  *  If TIM2 / PA0 are later configured in CubeMX, remove them there or here.
  ******************************************************************************
  */
#include "servo.h"
#include "main.h"

#define SERVO_PWM_PERIOD_US  20000U  /* 50 Hz */
#define SERVO_ANGLE_MAX      180

typedef struct
{
  uint8_t  span_deg;   /* sweep range width                 */
  uint8_t  step_ms;    /* time per 1 deg step (lower = faster) */
} Servo_ModeCfg;

/* Narrower range -> shorter step time -> faster motion.
   Full one-way sweep time: 180*20 = 3.6 s, 90*12 = 1.08 s, 45*8 = 0.36 s */
static const Servo_ModeCfg mode_cfg[SERVO_MODE_COUNT] =
{
  [SERVO_MODE_180] = { 180, 20 },
  [SERVO_MODE_90]  = {  90, 12 },
  [SERVO_MODE_45]  = {  45,  8 },
};

static Servo_Mode servo_mode;
static int16_t    servo_angle;
static int16_t    servo_min;
static int16_t    servo_max;
static int8_t     servo_dir;
static uint32_t   servo_last_tick;

static void Servo_WritePulse(int16_t angle_deg)
{
  uint32_t pulse = SERVO_PULSE_MIN_US +
                   ((uint32_t)angle_deg * (SERVO_PULSE_MAX_US - SERVO_PULSE_MIN_US)) / SERVO_ANGLE_MAX;
  TIM2->CCR1 = pulse;
}

static void Servo_PwmInit(void)
{
  GPIO_InitTypeDef GPIO_InitStruct = {0};

  __HAL_RCC_GPIOA_CLK_ENABLE();
  __HAL_RCC_TIM2_CLK_ENABLE();

  /* PA0 -> TIM2_CH1, alternate function push-pull */
  GPIO_InitStruct.Pin = GPIO_PIN_0;
  GPIO_InitStruct.Mode = GPIO_MODE_AF_PP;
  GPIO_InitStruct.Speed = GPIO_SPEED_FREQ_LOW;
  HAL_GPIO_Init(GPIOA, &GPIO_InitStruct);

  /* APB1 timer clock is doubled when the APB1 prescaler is not 1 */
  uint32_t tim_clk = HAL_RCC_GetPCLK1Freq();
  if ((RCC->CFGR & RCC_CFGR_PPRE1) != RCC_CFGR_PPRE1_DIV1)
  {
    tim_clk *= 2U;
  }

  TIM2->CR1   = 0;
  TIM2->PSC   = (tim_clk / 1000000U) - 1U;    /* 1 tick = 1 us */
  TIM2->ARR   = SERVO_PWM_PERIOD_US - 1U;     /* 20 ms period  */
  TIM2->CCMR1 = TIM_CCMR1_OC1M_2 | TIM_CCMR1_OC1M_1 | TIM_CCMR1_OC1PE;  /* PWM mode 1 */
  TIM2->CCER  = TIM_CCER_CC1E;
  Servo_WritePulse(servo_angle);
  TIM2->EGR   = TIM_EGR_UG;                   /* load PSC/ARR/CCR1 */
  TIM2->CR1   = TIM_CR1_ARPE | TIM_CR1_CEN;
}

void Servo_Init(Servo_Mode mode)
{
  servo_angle = SERVO_CENTER_DEG;
  servo_dir = 1;
  Servo_PwmInit();
  Servo_SetMode(mode);
  servo_last_tick = HAL_GetTick();
}

void Servo_SetMode(Servo_Mode mode)
{
  if (mode >= SERVO_MODE_COUNT)
  {
    return;
  }

  servo_mode = mode;
  servo_min = SERVO_CENTER_DEG - mode_cfg[mode].span_deg / 2;
  servo_max = servo_min + mode_cfg[mode].span_deg;

  if (servo_min < 0)
  {
    servo_min = 0;
  }
  if (servo_max > SERVO_ANGLE_MAX)
  {
    servo_max = SERVO_ANGLE_MAX;
  }
  /* If the current angle is outside the new range, Servo_Update()
     drives it back inside with the new speed. */
}

Servo_Mode Servo_GetMode(void)
{
  return servo_mode;
}

void Servo_NextMode(void)
{
  Servo_SetMode((Servo_Mode)((servo_mode + 1) % SERVO_MODE_COUNT));
}

void Servo_SetAngle(int16_t angle_deg)
{
  if (angle_deg < 0)
  {
    angle_deg = 0;
  }
  if (angle_deg > SERVO_ANGLE_MAX)
  {
    angle_deg = SERVO_ANGLE_MAX;
  }
  servo_angle = angle_deg;
  Servo_WritePulse(servo_angle);
}

int16_t Servo_GetAngle(void)
{
  return servo_angle;
}

uint8_t Servo_Update(void)
{
  uint32_t now = HAL_GetTick();

  if ((now - servo_last_tick) < mode_cfg[servo_mode].step_ms)
  {
    return 0;
  }
  servo_last_tick = now;

  if (servo_angle >= servo_max)
  {
    servo_dir = -1;
  }
  else if (servo_angle <= servo_min)
  {
    servo_dir = 1;
  }

  Servo_SetAngle(servo_angle + servo_dir);
  return 1;
}
