import { describe, it, expect, vi } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { MetadataSourceSelector } from "./MetadataSourceSelector";
import type { MetadataSearchServiceInfo } from "@/types/MetadataSearchServiceInfo";

const services: MetadataSearchServiceInfo[] = [
  { name: "Audible", enabled: true },
  { name: "Goodreads", enabled: true },
  { name: "Hardcover", enabled: false, disabledReason: "No API key configured" },
];

describe("MetadataSourceSelector", () => {
  it("renders every service as a badge", () => {
    render(
      <MetadataSourceSelector services={services} activeSources={[]} onToggleSource={vi.fn()} />,
    );

    expect(screen.getByText("Audible")).toBeInTheDocument();
    expect(screen.getByText("Goodreads")).toBeInTheDocument();
    expect(screen.getByText(/Hardcover/)).toBeInTheDocument();
  });

  it("shows the disabled reason for a source that is not configured", () => {
    render(
      <MetadataSourceSelector services={services} activeSources={[]} onToggleSource={vi.fn()} />,
    );

    expect(screen.getByText("Hardcover (No API key configured)")).toBeInTheDocument();
  });

  it("falls back to 'Unavailable' when a disabled service gives no reason", () => {
    render(
      <MetadataSourceSelector
        services={[{ name: "Hardcover", enabled: false }]}
        activeSources={[]}
        onToggleSource={vi.fn()}
      />,
    );

    expect(screen.getByText("Hardcover (Unavailable)")).toBeInTheDocument();
  });

  it("calls onToggleSource when a configured, unselected source is clicked", () => {
    const onToggleSource = vi.fn();
    render(
      <MetadataSourceSelector
        services={services}
        activeSources={[]}
        onToggleSource={onToggleSource}
      />,
    );

    fireEvent.click(screen.getByText("Audible"));

    expect(onToggleSource).toHaveBeenCalledWith("Audible");
  });

  it("calls onToggleSource for an already-selected source too (to deselect it)", () => {
    const onToggleSource = vi.fn();
    render(
      <MetadataSourceSelector
        services={services}
        activeSources={["Audible"]}
        onToggleSource={onToggleSource}
      />,
    );

    fireEvent.click(screen.getByText("Audible"));

    expect(onToggleSource).toHaveBeenCalledWith("Audible");
  });

  it("does not call onToggleSource for a source that is not configured", () => {
    const onToggleSource = vi.fn();
    render(
      <MetadataSourceSelector
        services={services}
        activeSources={[]}
        onToggleSource={onToggleSource}
      />,
    );

    fireEvent.click(screen.getByText(/Hardcover/));

    expect(onToggleSource).not.toHaveBeenCalled();
  });

  it("uses the default label when none is given", () => {
    render(
      <MetadataSourceSelector services={services} activeSources={[]} onToggleSource={vi.fn()} />,
    );

    expect(screen.getByText("Metadata Sources")).toBeInTheDocument();
  });

  it("uses a custom label when given", () => {
    render(
      <MetadataSourceSelector
        services={services}
        activeSources={[]}
        onToggleSource={vi.fn()}
        label="Search across"
      />,
    );

    expect(screen.getByText("Search across")).toBeInTheDocument();
    expect(screen.queryByText("Metadata Sources")).not.toBeInTheDocument();
  });
});
