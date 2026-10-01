/**
  ******************************************************************************
  * @file           : uart.h
  * @brief          : USART1 text link to the PC (TX -> PA9, RX <- PA10).
  *                   Receiving is interrupt driven into a ring buffer,
  *                   transmitting is blocking (polled).
  ******************************************************************************
  */
#ifndef __UART_H
#define __UART_H

#ifdef __cplusplus
extern "C" {
#endif

#include <stdint.h>

/* Received bytes waiting to be read by the main loop */
#define UART_RX_BUF_SIZE   64U    /* must be a power of two */

/* Longest command line, longer lines are truncated */
#define UART_LINE_MAX      32U

void        Uart_Init(uint32_t baud);

/* Blocking write of a zero terminated string */
void        Uart_Write(const char *s);

/* Call from the main loop as often as possible (non-blocking).
   Returns a complete line (without CR/LF) or NULL if there is none yet.
   The returned buffer is valid until the next call. */
const char *Uart_ReadLine(void);

#ifdef __cplusplus
}
#endif

#endif /* __UART_H */
