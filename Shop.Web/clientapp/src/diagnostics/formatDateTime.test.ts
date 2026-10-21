import { describe, expect, it } from "vitest";
import { formatDiagnosticDateTime } from "./formatDateTime";

describe("formatDiagnosticDateTime", () => {
  it("formats UTC timestamps in Russian locale at UTC+7 with milliseconds", () => {
    expect(formatDiagnosticDateTime("2026-10-20T10:05:06.123Z")).toBe(
      "20.10.2026 17:05:06.123",
    );
  });

  it("pads missing milliseconds to three digits", () => {
    expect(formatDiagnosticDateTime("2026-10-20T10:05:06Z")).toBe(
      "20.10.2026 17:05:06.000",
    );
  });

  it("converts timestamps across the UTC+7 date boundary", () => {
    expect(formatDiagnosticDateTime("2026-10-20T17:05:06.007Z")).toBe(
      "21.10.2026 00:05:06.007",
    );
  });

  it("returns a dash for missing or invalid timestamps", () => {
    expect(formatDiagnosticDateTime(null)).toBe("—");
    expect(formatDiagnosticDateTime("invalid")).toBe("—");
  });
});
