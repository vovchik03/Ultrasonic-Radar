/**
  ******************************************************************************
  * @file           : cmd.c
  * @brief          : Text command handler for the radar (see cmd.h).
  *
  *  No printf: numbers are formatted by hand to keep the firmware small.
  ******************************************************************************
  */
#include "cmd.h"
#include "scan.h"
#include <stdint.h>
#include <stddef.h>

static uint8_t cmd_stream;    /* 1 = STREAM ON */

static const char *Cmd_SkipSpaces(const char *p)
{
  while ((*p == ' ') || (*p == '\t'))
  {
    p++;
  }
  return p;
}

/* Compares the command word case-insensitively.
   Returns the position right after the word, or NULL if it does not match. */
static const char *Cmd_MatchWord(const char *line, const char *word)
{
  while (*word != '\0')
  {
    char c = *line;
    if ((c >= 'a') && (c <= 'z'))
    {
      c = (char)(c - 'a' + 'A');
    }
    if (c != *word)
    {
      return NULL;
    }
    line++;
    word++;
  }

  /* The word must end here, "SCANX" is not "SCAN" */
  if ((*line != '\0') && (*line != ' ') && (*line != '\t'))
  {
    return NULL;
  }
  return line;
}

/* Parses up to 3 decimal digits. Returns 1 on success and moves *pp past them. */
static uint8_t Cmd_ParseNumber(const char **pp, uint16_t *value)
{
  const char *p = *pp;
  uint16_t v = 0;
  uint8_t  digits = 0;

  while ((*p >= '0') && (*p <= '9'))
  {
    if (++digits > 3U)
    {
      return 0;
    }
    v = (uint16_t)(v * 10U + (uint16_t)(*p - '0'));
    p++;
  }

  if (digits == 0U)
  {
    return 0;
  }

  *value = v;
  *pp = p;
  return 1;
}

/* Writes the decimal value at dst, returns the position after the last digit */
static char *Cmd_AppendUint(char *dst, uint16_t value)
{
  char     tmp[5];
  uint8_t  n = 0;

  do
  {
    tmp[n++] = (char)('0' + (value % 10U));
    value /= 10U;
  } while (value != 0U);

  while (n > 0U)
  {
    *dst++ = tmp[--n];
  }
  return dst;
}

static void Cmd_Scan(const char *args, Cmd_WriteFn write)
{
  const char *p = Cmd_SkipSpaces(args);
  uint16_t from;
  uint16_t to;

  if (!Cmd_ParseNumber(&p, &from))
  {
    write("ERR ARGS\r\n");
    return;
  }

  while ((*p == ' ') || (*p == '\t') || (*p == '-') || (*p == ','))
  {
    p++;
  }

  if (!Cmd_ParseNumber(&p, &to) || (*Cmd_SkipSpaces(p) != '\0'))
  {
    write("ERR ARGS\r\n");
    return;
  }

  if ((from > SCAN_ANGLE_MAX) || (to > SCAN_ANGLE_MAX))
  {
    write("ERR RANGE\r\n");
    return;
  }

  Scan_Start((uint8_t)from, (uint8_t)to);

  /* Reply with the sector as it is applied (from <= to) */
  char  buf[24] = "OK SCAN ";
  char *end = Cmd_AppendUint(&buf[8], Scan_GetFrom());
  *end++ = ' ';
  end = Cmd_AppendUint(end, Scan_GetTo());
  *end++ = '\r';
  *end++ = '\n';
  *end = '\0';
  write(buf);
}

/* Parses the single "<angle>" argument of GOTO / HOLD.
   Returns 1 on success, sends the error reply and returns 0 otherwise. */
static uint8_t Cmd_ParseAngleArg(const char *args, uint8_t *angle, Cmd_WriteFn write)
{
  const char *p = Cmd_SkipSpaces(args);
  uint16_t value;

  if (!Cmd_ParseNumber(&p, &value) || (*Cmd_SkipSpaces(p) != '\0'))
  {
    write("ERR ARGS\r\n");
    return 0;
  }

  if (value > SCAN_ANGLE_MAX)
  {
    write("ERR RANGE\r\n");
    return 0;
  }

  *angle = (uint8_t)value;
  return 1;
}

/* Sends "<prefix><value>\r\n", e.g. "OK HOLD 120" */
static void Cmd_ReplyNumber(const char *prefix, uint16_t value, Cmd_WriteFn write)
{
  char  buf[8];
  char *end = Cmd_AppendUint(buf, value);
  *end++ = '\r';
  *end++ = '\n';
  *end = '\0';

  write(prefix);
  write(buf);
}

static void Cmd_GoTo(const char *args, Cmd_WriteFn write)
{
  uint8_t angle;

  if (Cmd_ParseAngleArg(args, &angle, write))
  {
    Scan_GoTo(angle);
    Cmd_ReplyNumber("OK GOTO ", angle, write);
  }
}

static void Cmd_Hold(const char *args, Cmd_WriteFn write)
{
  uint8_t angle;

  if (Cmd_ParseAngleArg(args, &angle, write))
  {
    Scan_Hold(angle);
    Cmd_ReplyNumber("OK HOLD ", angle, write);
  }
}

static void Cmd_Speed(const char *args, Cmd_WriteFn write)
{
  const char *p = Cmd_SkipSpaces(args);
  uint16_t value;

  if (!Cmd_ParseNumber(&p, &value) || (*Cmd_SkipSpaces(p) != '\0'))
  {
    write("ERR ARGS\r\n");
    return;
  }

  if (value > SCAN_SPEED_MAX_DPS)
  {
    write("ERR RANGE\r\n");
    return;
  }

  Scan_SetSpeed(value);
  Cmd_ReplyNumber("OK SPEED ", Scan_GetSpeed(), write);
}

static void Cmd_Step(const char *args, Cmd_WriteFn write)
{
  const char *p = Cmd_SkipSpaces(args);
  uint16_t value;

  if (!Cmd_ParseNumber(&p, &value) || (*Cmd_SkipSpaces(p) != '\0'))
  {
    write("ERR ARGS\r\n");
    return;
  }

  if ((value == 0U) || (value > SCAN_STEP_MAX_DEG))
  {
    write("ERR RANGE\r\n");
    return;
  }

  Scan_SetStep((uint8_t)value);
  Cmd_ReplyNumber("OK STEP ", Scan_GetStep(), write);
}

static void Cmd_Get(Cmd_WriteFn write)
{
  /* Sent point by point, so no big buffer is needed:
     a full 0..180 sector at 9 deg is ~200 bytes, ~17 ms at 115200 baud */
  write("DATA");

  /* Same grid as the scanner: from, from + step, ..., to */
  uint8_t angle = Scan_GetFrom();
  uint8_t to = Scan_GetTo();
  uint8_t step = Scan_GetStep();
  for (;;)
  {
    char  buf[12];
    char *end = buf;

    *end++ = ' ';
    end = Cmd_AppendUint(end, angle);
    *end++ = ':';
    end = Cmd_AppendUint(end, Scan_GetDistanceMm(angle));
    *end = '\0';
    write(buf);

    if (angle >= to)
    {
      break;
    }
    angle = ((uint16_t)angle + step > to) ? to : (uint8_t)(angle + step);
  }

  write("\r\n");
}

static void Cmd_Stream(const char *args, Cmd_WriteFn write)
{
  const char *p = Cmd_SkipSpaces(args);
  const char *end;
  uint8_t     enable;

  if ((end = Cmd_MatchWord(p, "ON")) != NULL)
  {
    enable = 1;
  }
  else if ((end = Cmd_MatchWord(p, "OFF")) != NULL)
  {
    enable = 0;
  }
  else
  {
    write("ERR ARGS\r\n");
    return;
  }

  if (*Cmd_SkipSpaces(end) != '\0')
  {
    write("ERR ARGS\r\n");
    return;
  }

  cmd_stream = enable;
  write(enable ? "OK STREAM ON\r\n" : "OK STREAM OFF\r\n");
}

void Cmd_ReportPoint(uint8_t angle, uint16_t distance_mm, Cmd_WriteFn write)
{
  if (!cmd_stream)
  {
    return;
  }

  /* Longest line: "P 180 65535\r\n" */
  char  buf[16] = "P ";
  char *end = Cmd_AppendUint(&buf[2], angle);
  *end++ = ' ';
  end = Cmd_AppendUint(end, distance_mm);
  *end++ = '\r';
  *end++ = '\n';
  *end = '\0';
  write(buf);
}

void Cmd_Process(const char *line, Cmd_WriteFn write)
{
  const char *p = Cmd_SkipSpaces(line);
  const char *args;

  if ((args = Cmd_MatchWord(p, "SCAN")) != NULL)
  {
    Cmd_Scan(args, write);
  }
  else if ((args = Cmd_MatchWord(p, "GOTO")) != NULL)
  {
    Cmd_GoTo(args, write);
  }
  else if ((args = Cmd_MatchWord(p, "HOLD")) != NULL)
  {
    Cmd_Hold(args, write);
  }
  else if ((args = Cmd_MatchWord(p, "SPEED")) != NULL)
  {
    Cmd_Speed(args, write);
  }
  else if ((args = Cmd_MatchWord(p, "STEP")) != NULL)
  {
    Cmd_Step(args, write);
  }
  else if ((args = Cmd_MatchWord(p, "STOP")) != NULL)
  {
    Scan_Stop();
    write("OK STOP\r\n");
  }
  else if ((args = Cmd_MatchWord(p, "GET")) != NULL)
  {
    Cmd_Get(write);
  }
  else if ((args = Cmd_MatchWord(p, "STREAM")) != NULL)
  {
    Cmd_Stream(args, write);
  }
  else
  {
    write("ERR UNKNOWN\r\n");
  }
}
