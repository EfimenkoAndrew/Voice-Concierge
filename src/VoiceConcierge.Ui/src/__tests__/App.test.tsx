import { describe, it, expect } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { App } from "../App";

describe("App", () => {
  it("renders the admin shell with all nav tabs (4-1)", () => {
    render(<App />);
    expect(screen.getByText(/Concierge Admin/i)).toBeInTheDocument();
    for (const t of ["FAQ", "Unanswered", "Voices", "Playground"])
      expect(screen.getByRole("button", { name: t })).toBeInTheDocument();
  });

  it("Playground tab shows the TEST MODE label (7-1 / AP-16)", () => {
    render(<App />);
    fireEvent.click(screen.getByRole("button", { name: "Playground" }));
    expect(screen.getByText(/TEST MODE/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Start conversation/i })).toBeInTheDocument();
  });
});
