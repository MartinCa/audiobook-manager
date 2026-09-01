import { describe, it, expect, vi } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { LibraryViewTabs } from "./LibraryViewTabs";

const mockNavigate = vi.fn();
vi.mock("@tanstack/react-router", () => ({
  useNavigate: () => mockNavigate,
}));

describe("LibraryViewTabs", () => {
  it("renders Books, Series, and Authors tabs with active state", () => {
    render(<LibraryViewTabs activeTab="books" />);

    expect(screen.getByRole("tab", { name: /books/i })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: /series/i })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: /authors/i })).toBeInTheDocument();
  });

  it("navigates to the appropriate route when a tab is clicked", () => {
    render(<LibraryViewTabs activeTab="books" />);

    const seriesTab = screen.getByRole("tab", { name: /series/i });
    fireEvent.click(seriesTab);
    expect(mockNavigate).toHaveBeenCalledWith({ to: "/library/series" });

    const authorsTab = screen.getByRole("tab", { name: /authors/i });
    fireEvent.click(authorsTab);
    expect(mockNavigate).toHaveBeenCalledWith({ to: "/library/authors" });
  });
});
