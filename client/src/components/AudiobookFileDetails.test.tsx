import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { AudiobookFileDetails } from "./AudiobookFileDetails";

describe("AudiobookFileDetails", () => {
  it("renders duration, file size, and file path", () => {
    render(
      <AudiobookFileDetails
        filePath="/audiobooks/Author/Book/file.m4b"
        sizeInBytes={10485760}
        durationInSeconds={3665}
      />,
    );

    expect(screen.getByText("Technical Details")).toBeInTheDocument();
    expect(screen.getByText("Duration:")).toBeInTheDocument();
    expect(screen.getByText("1h 1m 5s")).toBeInTheDocument();
    expect(screen.getByText("File Size:")).toBeInTheDocument();
    expect(screen.getByText("10.00 MB")).toBeInTheDocument();
    expect(screen.getByText("File Path:")).toBeInTheDocument();
    expect(screen.getByText("/audiobooks/Author/Book/file.m4b")).toBeInTheDocument();
  });

  it("handles unknown duration and null path gracefully", () => {
    render(<AudiobookFileDetails filePath={null} sizeInBytes={2048} durationInSeconds={null} />);

    expect(screen.getByText("Unknown")).toBeInTheDocument();
    expect(screen.getByText("2.00 KB")).toBeInTheDocument();
    expect(screen.queryByText("File Path:")).not.toBeInTheDocument();
  });
});
