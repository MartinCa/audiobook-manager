import { describe, it, expect, vi } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { LibraryToolsMenu } from "./LibraryToolsMenu";

const mockNavigate = vi.fn();
vi.mock("@tanstack/react-router", () => ({
  useNavigate: () => mockNavigate,
}));

describe("LibraryToolsMenu", () => {
  it("renders the Tools button and opens dropdown menu with items", async () => {
    render(<LibraryToolsMenu />);

    const toolsBtn = screen.getByRole("button", { name: /tools/i });
    expect(toolsBtn).toBeInTheDocument();

    fireEvent.click(toolsBtn);

    expect(await screen.findByText("Consistency Check")).toBeInTheDocument();
    expect(screen.getByText("Missing Tags")).toBeInTheDocument();
    expect(screen.getByText("Similar Values")).toBeInTheDocument();

    fireEvent.click(screen.getByText("Consistency Check"));
    expect(mockNavigate).toHaveBeenCalledWith({ to: "/library/consistency" });
  });
});
