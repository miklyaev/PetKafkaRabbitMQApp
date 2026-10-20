import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { Header } from "./Header";
import { MemoryRouter } from "react-router-dom";

describe("Header", () => {
  it("показывает все страницы и выделяет текущую семантически", () => {
    render(
      <MemoryRouter initialEntries={["/brokers/kafka"]}>
        <Header connected />
      </MemoryRouter>,
    );
    const nav = screen.getByRole("navigation", { name: "Основная навигация" });
    expect(nav).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Заказы" })).toHaveAttribute(
      "href",
      "/",
    );
    expect(screen.getByRole("link", { name: "RabbitMQ" })).toHaveAttribute(
      "href",
      "/brokers/rabbitmq",
    );
    expect(screen.getByRole("link", { name: "Kafka" })).toHaveAttribute(
      "aria-current",
      "page",
    );
    expect(
      screen.getByRole("link", { name: "Путь сообщения" }),
    ).toHaveAttribute("href", "/brokers/flow");
  });
  it("сообщает «на связи», когда SignalR подключён", () => {
    render(
      <MemoryRouter>
        <Header connected />
      </MemoryRouter>,
    );
    // role="status" — индикатор живости уведомлений; проверяем через роль,
    // а не className: текст — это и есть поведение компонента.
    expect(screen.getByRole("status")).toHaveTextContent(
      "Уведомления: на связи",
    );
  });

  it("сообщает «нет связи», когда подключения нет", () => {
    render(
      <MemoryRouter>
        <Header connected={false} />
      </MemoryRouter>,
    );
    expect(screen.getByRole("status")).toHaveTextContent(
      "Уведомления: нет связи",
    );
  });
});
