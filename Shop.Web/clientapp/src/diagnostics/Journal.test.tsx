import { render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { Journal } from "./Journal";
import { useJournal } from "./useJournal";

vi.mock("./useJournal", () => ({
  useJournal: vi.fn(),
}));

const event = {
  eventId: "event-1",
  service: "Shop.Orders",
  instanceId: "instance-1",
  broker: "rabbitmq" as const,
  operation: "publish" as const,
  phase: "completed" as const,
  messageType: "OrderCreated",
  messageId: null,
  requestId: null,
  correlationId: null,
  conversationId: null,
  orderId: null,
  observedAt: "2026-10-20T10:05:06Z",
  durationMs: null,
  address: null,
  partition: null,
  offset: null,
  payload: null,
  payloadTruncated: false,
  headers: {},
  error: null,
};

beforeEach(() => {
  vi.mocked(useJournal).mockReturnValue({
    events: [{ sequence: 1, event }],
    connected: true,
    error: "",
    gap: false,
    dropped: 0,
    loading: false,
    refresh: vi.fn(),
  });
});

describe("Journal", () => {
  it("renders event time in Russian format at UTC+7 instead of the raw ISO value", () => {
    render(<Journal orderId="" />);

    const row = screen.getByRole("row", { name: /Shop\.Orders/ });
    // The sequence and timestamp share one cell, so assert on the cell text rather than an exact text node.
    const timeCell = row.querySelector("td");

    expect(timeCell).toHaveTextContent("20.10.2026 17:05:06.000");
    expect(timeCell).not.toHaveTextContent(event.observedAt);
  });
});
