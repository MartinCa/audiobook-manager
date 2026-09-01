import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { OperationProgressBar } from "./OperationProgressBar";

describe("OperationProgressBar", () => {
  it("renders standard mode with card container and full count formatting", () => {
    render(<OperationProgressBar processed={45} total={100} label="Organizing..." />);

    expect(screen.getByText("Organizing...")).toBeInTheDocument();
    expect(screen.getByText("45 / 100 (45%)")).toBeInTheDocument();
  });

  it("renders compact mode with sleek percentage format and without card counts", () => {
    render(<OperationProgressBar compact processed={60} total={100} label="Saving tags..." />);

    expect(screen.getByText("Saving tags...")).toBeInTheDocument();
    expect(screen.getByText("60%")).toBeInTheDocument();
    expect(screen.queryByText("60 / 100 (60%)")).not.toBeInTheDocument();
  });

  it("renders subText when provided", () => {
    render(
      <OperationProgressBar
        compact
        processed={30}
        total={100}
        label="Processing"
        subText="Step 2 of 5"
      />,
    );

    expect(screen.getByText("Step 2 of 5")).toBeInTheDocument();
  });
});
