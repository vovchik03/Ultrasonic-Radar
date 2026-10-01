/**
  ******************************************************************************
  * @file           : cmd.h
  * @brief          : Text command handler for the radar.
  *                   Does not depend on the transport: takes one line and
  *                   writes the reply through the given function, so the same
  *                   handler works over UART now and over USB later.
  *
  *  Commands (case-insensitive, one per line, CR and/or LF terminated):
  *
  *    SCAN <from> <to>   sweep the sector, angles 0..180, order does not
  *                       matter, separator: space, '-' or ','
  *                       e.g. "SCAN 40 70", "scan 40-70"
  *                       -> OK SCAN 40 70
  *    STOP               stop sweeping, servo holds its angle
  *                       -> OK STOP
  *    GET                distances of the current sector, "angle:mm" pairs,
  *                       0 = no echo / not measured yet
  *                       -> DATA 40:523 41:518 42:0 ... 70:1210
  *
  *  Errors: ERR UNKNOWN, ERR ARGS (bad format), ERR RANGE (angle > 180)
  ******************************************************************************
  */
#ifndef __CMD_H
#define __CMD_H

#ifdef __cplusplus
extern "C" {
#endif

typedef void (*Cmd_WriteFn)(const char *s);

/* Execute one command line and send the reply through write() */
void Cmd_Process(const char *line, Cmd_WriteFn write);

#ifdef __cplusplus
}
#endif

#endif /* __CMD_H */
