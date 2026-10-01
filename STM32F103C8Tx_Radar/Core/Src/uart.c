/**
  ******************************************************************************
  * @file           : uart.c
  * @brief          : USART1 text link to the PC (TX -> PA9, RX <- PA10).
  *
  *  RX interrupt puts every byte into a ring buffer, the main loop assembles
  *  lines from it in Uart_ReadLine(). TX is polled: at 115200 baud a short
  *  reply takes ~1-2 ms, which is fine for a command interface.
  *
  *  USART1 is configured directly through registers, so the driver does not
  *  depend on HAL_UART_MODULE_ENABLED and survives CubeMX code regeneration.
  *  If USART1 / PA9 / PA10 are later configured in CubeMX, remove them there
  *  or here (CubeMX would also generate its own USART1_IRQHandler).
  ******************************************************************************
  */
#include "uart.h"
#include "main.h"

#define UART_RX_MASK   (UART_RX_BUF_SIZE - 1U)

static volatile uint8_t  rx_buf[UART_RX_BUF_SIZE];
static volatile uint16_t rx_head;   /* written by the ISR       */
static volatile uint16_t rx_tail;   /* written by the main loop */

static char     line_buf[UART_LINE_MAX];
static uint16_t line_len;

void Uart_Init(uint32_t baud)
{
  GPIO_InitTypeDef GPIO_InitStruct = {0};

  __HAL_RCC_GPIOA_CLK_ENABLE();   /* UART_TX_GPIO_Port, UART_RX_GPIO_Port */
  __HAL_RCC_USART1_CLK_ENABLE();

  /* PA9 -> USART1_TX, alternate function push-pull */
  GPIO_InitStruct.Pin = UART_TX_Pin;
  GPIO_InitStruct.Mode = GPIO_MODE_AF_PP;
  GPIO_InitStruct.Speed = GPIO_SPEED_FREQ_HIGH;
  HAL_GPIO_Init(UART_TX_GPIO_Port, &GPIO_InitStruct);

  /* PA10 -> USART1_RX, input with pull-up (UART line idles high) */
  GPIO_InitStruct.Pin = UART_RX_Pin;
  GPIO_InitStruct.Mode = GPIO_MODE_INPUT;
  GPIO_InitStruct.Pull = GPIO_PULLUP;
  HAL_GPIO_Init(UART_RX_GPIO_Port, &GPIO_InitStruct);

  rx_head = 0;
  rx_tail = 0;
  line_len = 0;

  /* 8N1, oversampling by 16: BRR = fPCLK / baud (USART1 sits on APB2) */
  USART1->CR1 = 0;
  USART1->CR2 = 0;
  USART1->CR3 = 0;
  USART1->BRR = (HAL_RCC_GetPCLK2Freq() + baud / 2U) / baud;
  USART1->CR1 = USART_CR1_UE | USART_CR1_TE | USART_CR1_RE | USART_CR1_RXNEIE;

  HAL_NVIC_SetPriority(USART1_IRQn, 1, 0);
  HAL_NVIC_EnableIRQ(USART1_IRQn);
}

void Uart_Write(const char *s)
{
  while (*s != '\0')
  {
    while ((USART1->SR & USART_SR_TXE) == 0U)
    {
    }
    USART1->DR = (uint8_t)*s++;
  }
}

const char *Uart_ReadLine(void)
{
  while (rx_tail != rx_head)
  {
    char c = (char)rx_buf[rx_tail];
    rx_tail = (rx_tail + 1U) & UART_RX_MASK;

    if ((c == '\r') || (c == '\n'))
    {
      if (line_len == 0U)
      {
        continue;   /* empty line or the LF of a CR+LF pair */
      }
      line_buf[line_len] = '\0';
      line_len = 0;
      return line_buf;
    }

    if ((c == '\b') || (c == 0x7F))
    {
      /* Backspace typed in a terminal */
      if (line_len > 0U)
      {
        line_len--;
      }
      continue;
    }

    if (line_len < (UART_LINE_MAX - 1U))
    {
      line_buf[line_len++] = c;
    }
  }

  return NULL;
}

void USART1_IRQHandler(void)
{
  uint32_t sr = USART1->SR;

  if ((sr & (USART_SR_RXNE | USART_SR_ORE | USART_SR_NE | USART_SR_FE)) != 0U)
  {
    /* Reading SR then DR clears RXNE and the error flags */
    uint8_t  c = (uint8_t)USART1->DR;
    uint16_t next = (rx_head + 1U) & UART_RX_MASK;

    /* Store only clean bytes; if the buffer is full the byte is dropped */
    if (((sr & (USART_SR_NE | USART_SR_FE)) == 0U) && (next != rx_tail))
    {
      rx_buf[rx_head] = c;
      rx_head = next;
    }
  }
}
