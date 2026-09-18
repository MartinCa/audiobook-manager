import { describe, it, expect, vi } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { AppDialog } from "./AppDialog";

describe("AppDialog", () => {
  it("renders title, description, body and footer when open", () => {
    render(
      <AppDialog
        open={true}
        onOpenChange={vi.fn()}
        title="Dialog title"
        description="Dialog description"
        footer={<button>Footer action</button>}
      >
        <p>Body content</p>
      </AppDialog>,
    );

    expect(screen.getByText("Dialog title")).toBeInTheDocument();
    expect(screen.getByText("Dialog description")).toBeInTheDocument();
    expect(screen.getByText("Body content")).toBeInTheDocument();
    expect(screen.getByText("Footer action")).toBeInTheDocument();
  });

  it("renders nothing when closed", () => {
    render(
      <AppDialog open={false} onOpenChange={vi.fn()} title="Dialog title">
        <p>Body content</p>
      </AppDialog>,
    );

    expect(screen.queryByText("Dialog title")).not.toBeInTheDocument();
  });

  it("omits the footer row entirely when no footer is given", () => {
    const { container } = render(
      <AppDialog open={true} onOpenChange={vi.fn()} title="Dialog title">
        <p>Body content</p>
      </AppDialog>,
    );

    expect(container.querySelector(".border-t")).not.toBeInTheDocument();
  });

  it("calls onOpenChange(false) when the built-in close button is used", () => {
    const onOpenChange = vi.fn();
    render(
      <AppDialog open={true} onOpenChange={onOpenChange} title="Dialog title">
        <p>Body content</p>
      </AppDialog>,
    );

    fireEvent.click(screen.getByRole("button", { name: "Close" }));
    expect(onOpenChange.mock.calls[0]?.[0]).toBe(false);
  });
});
