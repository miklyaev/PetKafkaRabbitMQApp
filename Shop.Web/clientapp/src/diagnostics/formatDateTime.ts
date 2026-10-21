const diagnosticDateTimeFormat = new Intl.DateTimeFormat("ru-RU", {
  timeZone: "Asia/Krasnoyarsk",
  day: "2-digit",
  month: "2-digit",
  year: "numeric",
  hour: "2-digit",
  minute: "2-digit",
  second: "2-digit",
  hourCycle: "h23",
});

export function formatDiagnosticDateTime(value: string | null): string {
  if (!value) return "—";

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return "—";

  const parts = Object.fromEntries(
    diagnosticDateTimeFormat
      .formatToParts(date)
      .map(({ type, value: partValue }) => [type, partValue]),
  );
  const milliseconds = String(date.getUTCMilliseconds()).padStart(3, "0");

  return `${parts.day}.${parts.month}.${parts.year} ${parts.hour}:${parts.minute}:${parts.second}.${milliseconds}`;
}
