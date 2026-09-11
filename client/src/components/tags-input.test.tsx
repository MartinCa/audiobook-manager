import { describe, it, expect, vi } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { useState } from "react";
import { TagsInput, type TagsInputProps } from "./tags-input";

type ControlledTagsInputProps = {
  initial?: string[];
  onValueChange?: (value: string[]) => void;
} & Partial<Omit<TagsInputProps, "value" | "onValueChange">>;

function ControlledTagsInput({
  initial = [],
  onValueChange,
  placeholder = "Fantasy, Fiction",
  ...rest
}: ControlledTagsInputProps) {
  const [value, setValue] = useState(initial);
  return (
    <TagsInput
      value={value}
      onValueChange={(v) => {
        setValue(v);
        onValueChange?.(v);
      }}
      placeholder={placeholder}
      {...rest}
    />
  );
}

// --- FormKit drag simulation -------------------------------------------------
//
// FormKit 0.6.1 is native HTML5-drag-first: a drag starts with pointerdown on the drag handle
// (which validates `dragHandle` against the event's composedPath and marks the node
// `draggable`), then dragstart on the node, then a live reorder on dragover, and finally
// drop/dragend. jsdom cannot produce a real native drag, so the tests replay the same event
// sequence with MouseEvent objects (jsdom has no DragEvent class - formkit only reads
// clientX/clientY/preventDefault off it, which MouseEvent provides).
//
// validateSort decides the drop direction from getBoundingClientRect geometry, which jsdom
// reports as all-zeros, so each node's rect is stubbed to a synthetic coordinate grid
// (chip i at x = i*100, 60px wide). Dragging chip i over chip j to the right of j's right
// edge (clientX past the target's rect) makes the sort fire; the drop threshold stays 0.
function setChipRect(node: HTMLElement, x: number) {
  node.getBoundingClientRect = () =>
    ({
      x,
      y: 0,
      left: x,
      top: 0,
      right: x + 60,
      bottom: 20,
      width: 60,
      height: 20,
      toJSON() {},
    }) satisfies DOMRect;
}

function chipNodeFor(label: string): HTMLElement {
  const node = screen.getByLabelText(label).parentElement;
  if (!node) throw new Error(`no chip node found for ${label}`);
  return node;
}

// Performs a full native drag of `fromLabel`'s chip over `toLabel`'s chip, preserving the
// order parameter just to lay out the coordinate grid. `clientX` should point right of the
// current drag origin so the sort actually fires (leftward drags use clientX inside the
// target; see the per-test call sites).
function dragChip(order: string[], fromLabel: string, toLabel: string, clientX: number) {
  const handle = screen.getByLabelText(`Reorder: ${fromLabel}`);
  const fromNode = chipNodeFor(`Reorder: ${fromLabel}`);
  const toNode = chipNodeFor(`Reorder: ${toLabel}`);
  setChipRect(fromNode, order.indexOf(fromLabel) * 100);
  setChipRect(toNode, order.indexOf(toLabel) * 100);
  fireEvent(handle, new PointerEvent("pointerdown", { bubbles: true }));
  fireEvent(fromNode, new MouseEvent("dragstart", { bubbles: true }));
  fireEvent(toNode, new MouseEvent("dragover", { clientX, clientY: 5, bubbles: true }));
  fireEvent(toNode, new MouseEvent("drop", { bubbles: true }));
  fireEvent(toNode, new MouseEvent("dragend", { bubbles: true }));
}

describe("TagsInput", () => {
  it("renders existing values as chips", () => {
    render(<ControlledTagsInput initial={["Fantasy", "Fiction"]} />);
    expect(screen.getByText("Fantasy")).toBeInTheDocument();
    expect(screen.getByText("Fiction")).toBeInTheDocument();
  });

  it("commits the typed text as a new chip on Enter and clears the draft", () => {
    const onValueChange = vi.fn();
    render(<ControlledTagsInput onValueChange={onValueChange} />);

    const input = screen.getByPlaceholderText("Fantasy, Fiction");
    fireEvent.change(input, { target: { value: "Heartfelt" } });
    fireEvent.keyDown(input, { key: "Enter" });

    expect(onValueChange).toHaveBeenCalledWith(["Heartfelt"]);
    expect(screen.getByText("Heartfelt")).toBeInTheDocument();
    expect(input).toHaveValue("");
  });

  it("commits the draft on blur too, not just Enter", () => {
    render(<ControlledTagsInput />);

    const input = screen.getByPlaceholderText("Fantasy, Fiction");
    fireEvent.change(input, { target: { value: "Thought-Provoking" } });
    fireEvent.blur(input);

    expect(screen.getByText("Thought-Provoking")).toBeInTheDocument();
  });

  it("does not add a duplicate chip (case-insensitive)", () => {
    const onValueChange = vi.fn();
    render(<ControlledTagsInput initial={["Fantasy"]} onValueChange={onValueChange} />);

    const input = screen.getByRole("textbox");
    fireEvent.change(input, { target: { value: "fantasy" } });
    fireEvent.keyDown(input, { key: "Enter" });

    expect(onValueChange).not.toHaveBeenCalled();
    expect(screen.getAllByText(/fantasy/i)).toHaveLength(1);
  });

  it("removes the last chip on Backspace when the draft is empty", () => {
    const onValueChange = vi.fn();
    render(<ControlledTagsInput initial={["Fantasy", "Fiction"]} onValueChange={onValueChange} />);

    const input = screen.getByRole("textbox");
    fireEvent.keyDown(input, { key: "Backspace" });

    expect(onValueChange).toHaveBeenCalledWith(["Fantasy"]);
  });

  it("removes a chip via its remove button", () => {
    const onValueChange = vi.fn();
    render(<ControlledTagsInput initial={["Fantasy", "Fiction"]} onValueChange={onValueChange} />);

    fireEvent.click(screen.getByLabelText("Remove Fantasy"));

    expect(onValueChange).toHaveBeenCalledWith(["Fiction"]);
  });

  it("ignores a Backspace on empty draft when there are no chips left", () => {
    const onValueChange = vi.fn();
    render(<ControlledTagsInput onValueChange={onValueChange} />);

    const input = screen.getByRole("textbox");
    fireEvent.keyDown(input, { key: "Backspace" });

    expect(onValueChange).not.toHaveBeenCalled();
  });

  describe("editing an existing chip", () => {
    it("clicking a chip turns it into an editable input pre-filled with its value", () => {
      render(<ControlledTagsInput initial={["Fantasy", "Fiction"]} />);

      fireEvent.click(screen.getByLabelText("Edit Fantasy"));

      expect(screen.getByDisplayValue("Fantasy")).toBeInTheDocument();
    });

    it("commits an edit in place on Enter, preserving the order of every other entry", () => {
      const onValueChange = vi.fn();
      render(
        <ControlledTagsInput initial={["Alpha", "Beta", "Gamma"]} onValueChange={onValueChange} />,
      );

      fireEvent.click(screen.getByLabelText("Edit Beta"));
      const editInput = screen.getByDisplayValue("Beta");
      fireEvent.change(editInput, { target: { value: "Beta Fixed" } });
      fireEvent.keyDown(editInput, { key: "Enter" });

      expect(onValueChange).toHaveBeenCalledWith(["Alpha", "Beta Fixed", "Gamma"]);
    });

    it("commits an edit on blur too, not just Enter", () => {
      const onValueChange = vi.fn();
      render(<ControlledTagsInput initial={["Fantasy"]} onValueChange={onValueChange} />);

      fireEvent.click(screen.getByLabelText("Edit Fantasy"));
      const editInput = screen.getByDisplayValue("Fantasy");
      fireEvent.change(editInput, { target: { value: "Fantasy Fixed" } });
      fireEvent.blur(editInput);

      expect(onValueChange).toHaveBeenCalledWith(["Fantasy Fixed"]);
    });

    it("cancels the edit on Escape without changing the value", () => {
      const onValueChange = vi.fn();
      render(<ControlledTagsInput initial={["Fantasy"]} onValueChange={onValueChange} />);

      fireEvent.click(screen.getByLabelText("Edit Fantasy"));
      const editInput = screen.getByDisplayValue("Fantasy");
      fireEvent.change(editInput, { target: { value: "Something Else" } });
      fireEvent.keyDown(editInput, { key: "Escape" });

      expect(onValueChange).not.toHaveBeenCalled();
      expect(screen.getByText("Fantasy")).toBeInTheDocument();
      expect(screen.queryByDisplayValue("Something Else")).not.toBeInTheDocument();
    });

    it("removes the entry when edited down to an empty value", () => {
      const onValueChange = vi.fn();
      render(
        <ControlledTagsInput initial={["Fantasy", "Fiction"]} onValueChange={onValueChange} />,
      );

      fireEvent.click(screen.getByLabelText("Edit Fantasy"));
      const editInput = screen.getByDisplayValue("Fantasy");
      fireEvent.change(editInput, { target: { value: "   " } });
      fireEvent.keyDown(editInput, { key: "Enter" });

      expect(onValueChange).toHaveBeenCalledWith(["Fiction"]);
    });

    it("rejects an edit that would duplicate another existing entry", () => {
      const onValueChange = vi.fn();
      render(
        <ControlledTagsInput initial={["Fantasy", "Fiction"]} onValueChange={onValueChange} />,
      );

      fireEvent.click(screen.getByLabelText("Edit Fantasy"));
      const editInput = screen.getByDisplayValue("Fantasy");
      fireEvent.change(editInput, { target: { value: "fiction" } });
      fireEvent.keyDown(editInput, { key: "Enter" });

      expect(onValueChange).not.toHaveBeenCalled();
      expect(screen.getByText("Fantasy")).toBeInTheDocument();
    });
  });

  describe("typeahead suggestions", () => {
    const authorNames = ["Brandon Sanderson", "Brandon Ellis", "Frank Herbert"];

    it("shows narrowed suggestions while typing a new entry", () => {
      render(<ControlledTagsInput suggestions={authorNames} />);

      const input = screen.getByRole("textbox");
      fireEvent.change(input, { target: { value: "Brandon" } });

      expect(screen.getByText("Brandon Sanderson")).toBeInTheDocument();
      expect(screen.getByText("Brandon Ellis")).toBeInTheDocument();
      expect(screen.queryByText("Frank Herbert")).not.toBeInTheDocument();
    });

    it("does not suggest a candidate that is already a committed chip", () => {
      render(<ControlledTagsInput initial={["Brandon Sanderson"]} suggestions={authorNames} />);

      const input = screen.getByRole("textbox");
      fireEvent.change(input, { target: { value: "Brandon" } });

      expect(screen.queryByRole("option", { name: "Brandon Sanderson" })).not.toBeInTheDocument();
      expect(screen.getByRole("option", { name: "Brandon Ellis" })).toBeInTheDocument();
    });

    it("clicking a suggestion commits it as a chip and clears the draft", () => {
      const onValueChange = vi.fn();
      render(<ControlledTagsInput suggestions={authorNames} onValueChange={onValueChange} />);

      const input = screen.getByRole("textbox");
      fireEvent.change(input, { target: { value: "Frank" } });
      fireEvent.pointerDown(screen.getByText("Frank Herbert"));

      expect(onValueChange).toHaveBeenCalledWith(["Frank Herbert"]);
      expect(input).toHaveValue("");
    });

    it("selecting a highlighted suggestion with Enter commits it", () => {
      const onValueChange = vi.fn();
      render(<ControlledTagsInput suggestions={authorNames} onValueChange={onValueChange} />);

      const input = screen.getByRole("textbox");
      fireEvent.change(input, { target: { value: "Brandon" } });
      fireEvent.keyDown(input, { key: "ArrowDown" });
      fireEvent.keyDown(input, { key: "Enter" });

      expect(onValueChange).toHaveBeenCalledWith(["Brandon Sanderson"]);
    });

    it("does not show suggestions when none are configured (Genres usage)", () => {
      render(<ControlledTagsInput />);

      const input = screen.getByRole("textbox");
      fireEvent.change(input, { target: { value: "Fantasy" } });

      expect(screen.queryByRole("listbox")).not.toBeInTheDocument();
    });

    it("also shows narrowed suggestions while editing an existing chip in place", () => {
      render(<ControlledTagsInput initial={["Brandon Sanderon"]} suggestions={authorNames} />);

      fireEvent.click(screen.getByLabelText("Edit Brandon Sanderon"));
      const editInput = screen.getByDisplayValue("Brandon Sanderon");
      fireEvent.change(editInput, { target: { value: "Brandon" } });

      expect(screen.getByRole("option", { name: "Brandon Sanderson" })).toBeInTheDocument();
      expect(screen.getByRole("option", { name: "Brandon Ellis" })).toBeInTheDocument();
    });

    it("does not exclude the entry's own current value from edit suggestions, only other entries", () => {
      render(
        <ControlledTagsInput
          initial={["Brandon Sanderson", "Frank Herbert"]}
          suggestions={authorNames}
        />,
      );

      fireEvent.click(screen.getByLabelText("Edit Frank Herbert"));
      const editInput = screen.getByDisplayValue("Frank Herbert");
      fireEvent.change(editInput, { target: { value: "Brandon" } });

      // "Brandon Sanderson" is excluded because it's a *different* committed chip; suggesting
      // it here would create a duplicate.
      expect(screen.queryByRole("option", { name: "Brandon Sanderson" })).not.toBeInTheDocument();
      expect(screen.getByRole("option", { name: "Brandon Ellis" })).toBeInTheDocument();
    });

    it("selecting a suggestion while editing replaces that entry in place, preserving order", () => {
      const onValueChange = vi.fn();
      render(
        <ControlledTagsInput
          initial={["Alpha", "Brandon Sanderon", "Gamma"]}
          suggestions={authorNames}
          onValueChange={onValueChange}
        />,
      );

      fireEvent.click(screen.getByLabelText("Edit Brandon Sanderon"));
      const editInput = screen.getByDisplayValue("Brandon Sanderon");
      fireEvent.change(editInput, { target: { value: "Brandon" } });
      fireEvent.pointerDown(screen.getByRole("option", { name: "Brandon Sanderson" }));

      expect(onValueChange).toHaveBeenCalledWith(["Alpha", "Brandon Sanderson", "Gamma"]);
    });

    it("selecting a highlighted edit suggestion with Enter commits it in place", () => {
      const onValueChange = vi.fn();
      render(
        <ControlledTagsInput
          initial={["Brandon Sanderon"]}
          suggestions={authorNames}
          onValueChange={onValueChange}
        />,
      );

      fireEvent.click(screen.getByLabelText("Edit Brandon Sanderon"));
      const editInput = screen.getByDisplayValue("Brandon Sanderon");
      fireEvent.change(editInput, { target: { value: "Brandon" } });
      fireEvent.keyDown(editInput, { key: "ArrowDown" });
      fireEvent.keyDown(editInput, { key: "Enter" });

      expect(onValueChange).toHaveBeenCalledWith(["Brandon Sanderson"]);
    });
  });

  describe("reordering", () => {
    it("does not render a drag handle when reorderable is not set (Genres usage)", () => {
      render(<ControlledTagsInput initial={["Fantasy", "Fiction"]} />);

      expect(screen.queryByLabelText("Reorder: Fantasy")).not.toBeInTheDocument();
    });

    it("renders a drag handle per chip when reorderable", () => {
      render(<ControlledTagsInput initial={["Fantasy", "Fiction"]} reorderable />);

      expect(screen.getByLabelText("Reorder: Fantasy")).toBeInTheDocument();
      expect(screen.getByLabelText("Reorder: Fiction")).toBeInTheDocument();
    });

    it("describes the drag handle honestly as a pointer-only control", () => {
      render(<ControlledTagsInput initial={["Fantasy"]} reorderable />);

      const handle = screen.getByLabelText("Reorder: Fantasy");
      // formkit 0.6.1 has no keyboard drag plugin, so the handle must not imply keyboard
      // reordering - the description says plainly it is a pointer gesture.
      expect(handle).toHaveAttribute("aria-description", expect.stringMatching(/pointer|drag/i));
      expect(handle.tagName).toBe("BUTTON");
      // A disabled field hides the handle entirely rather than rendering a dead one - covered
      // by the separate "hides the drag handle when disabled even if reorderable" test - so
      // there is no always-dead aria-disabled button in the tab order.
      expect(screen.queryByLabelText("Reorder: Fantasy")).toBeInTheDocument();
    });

    it("hides the drag handle when disabled even if reorderable", () => {
      render(<ControlledTagsInput initial={["Fantasy"]} reorderable disabled />);

      expect(screen.queryByLabelText("Reorder: Fantasy")).not.toBeInTheDocument();
    });

    it("reorders chips on drag and reports the new order through onValueChange", () => {
      const onValueChange = vi.fn();
      render(
        <ControlledTagsInput
          initial={["Alpha", "Beta", "Gamma"]}
          reorderable
          onValueChange={onValueChange}
        />,
      );

      dragChip(["Alpha", "Beta", "Gamma"], "Alpha", "Gamma", 250);

      expect(onValueChange).toHaveBeenCalledTimes(1);
      expect(onValueChange).toHaveBeenCalledWith(["Beta", "Gamma", "Alpha"]);
    });

    it("reorders leftwards too (dragging a later chip back over an earlier one)", () => {
      const onValueChange = vi.fn();
      render(
        <ControlledTagsInput
          initial={["Alpha", "Beta", "Gamma"]}
          reorderable
          onValueChange={onValueChange}
        />,
      );

      dragChip(["Alpha", "Beta", "Gamma"], "Gamma", "Alpha", 30);

      expect(onValueChange).toHaveBeenCalledWith(["Gamma", "Alpha", "Beta"]);
    });

    it("moves a chip from the middle of the list", () => {
      const onValueChange = vi.fn();
      render(
        <ControlledTagsInput
          initial={["Alpha", "Beta", "Gamma", "Delta"]}
          reorderable
          onValueChange={onValueChange}
        />,
      );

      dragChip(["Alpha", "Beta", "Gamma", "Delta"], "Beta", "Delta", 350);

      expect(onValueChange).toHaveBeenCalledWith(["Alpha", "Gamma", "Delta", "Beta"]);
    });

    it("keeps the reordered chips in the new visual order", () => {
      const onValueChange = vi.fn();
      render(
        <ControlledTagsInput
          initial={["Alpha", "Beta", "Gamma"]}
          reorderable
          onValueChange={onValueChange}
        />,
      );

      dragChip(["Alpha", "Beta", "Gamma"], "Alpha", "Gamma", 250);

      const wrapper = chipNodeFor("Reorder: Beta").parentElement!;
      const editLabels = Array.from(wrapper.children).map((child) =>
        (child as HTMLElement).querySelector('[aria-label^="Edit "]')?.getAttribute("aria-label"),
      );
      expect(editLabels).toEqual(["Edit Beta", "Edit Gamma", "Edit Alpha"]);
    });

    it("does not reorder when the drag is cancelled (drop on the origin)", () => {
      const onValueChange = vi.fn();
      render(
        <ControlledTagsInput
          initial={["Alpha", "Beta"]}
          reorderable
          onValueChange={onValueChange}
        />,
      );

      const handle = screen.getByLabelText("Reorder: Alpha");
      const fromNode = chipNodeFor("Reorder: Alpha");
      const toNode = chipNodeFor("Reorder: Beta");
      setChipRect(fromNode, 0);
      setChipRect(toNode, 100);
      fireEvent(handle, new PointerEvent("pointerdown", { bubbles: true }));
      fireEvent(fromNode, new MouseEvent("dragstart", { bubbles: true }));
      // No dragover onto a different node: dropping right away is a no-op.
      fireEvent(fromNode, new MouseEvent("drop", { bubbles: true }));
      fireEvent(fromNode, new MouseEvent("dragend", { bubbles: true }));

      expect(onValueChange).not.toHaveBeenCalled();
    });

    it("does not reorder when the drag is cancelled (dragover with a leftward corner check fails)", () => {
      const onValueChange = vi.fn();
      render(
        <ControlledTagsInput
          initial={["Alpha", "Beta"]}
          reorderable
          onValueChange={onValueChange}
        />,
      );

      const handle = screen.getByLabelText("Reorder: Alpha");
      const fromNode = chipNodeFor("Reorder: Alpha");
      const toNode = chipNodeFor("Reorder: Beta");
      setChipRect(fromNode, 0);
      setChipRect(toNode, 100);
      fireEvent(handle, new PointerEvent("pointerdown", { bubbles: true }));
      fireEvent(fromNode, new MouseEvent("dragstart", { bubbles: true }));
      // The pointer never actually crossed onto Beta: the dragover coordinates stay left of
      // Beta's rect, so validateSort refuses and no reorder happens.
      fireEvent(toNode, new MouseEvent("dragover", { clientX: 10, clientY: 5, bubbles: true }));
      fireEvent(toNode, new MouseEvent("drop", { bubbles: true }));
      fireEvent(toNode, new MouseEvent("dragend", { bubbles: true }));

      expect(onValueChange).not.toHaveBeenCalled();
    });

    describe("controlled value stays in step with formkit", () => {
      it("does not reorder from a chip that was added after the field first rendered", () => {
        const onValueChange = vi.fn();
        render(
          <ControlledTagsInput
            initial={["Alpha", "Beta"]}
            reorderable
            onValueChange={onValueChange}
          />,
        );

        const input = screen.getByRole("textbox");
        fireEvent.change(input, { target: { value: "Gamma" } });
        fireEvent.keyDown(input, { key: "Enter" });
        expect(screen.getByLabelText("Reorder: Gamma")).toBeInTheDocument();

        dragChip(["Alpha", "Beta", "Gamma"], "Gamma", "Alpha", 30);

        expect(onValueChange).toHaveBeenLastCalledWith(["Gamma", "Alpha", "Beta"]);
      });

      it("does not drag a chip that was removed externally since the last render", () => {
        const onValueChange = vi.fn();
        render(
          <ControlledTagsInput
            initial={["Alpha", "Beta", "Gamma"]}
            reorderable
            onValueChange={onValueChange}
          />,
        );

        fireEvent.click(screen.getByLabelText("Remove Beta"));

        dragChip(["Alpha", "Gamma"], "Gamma", "Alpha", 30);

        expect(onValueChange).toHaveBeenLastCalledWith(["Gamma", "Alpha"]);
      });

      it("reorders with the value an external edit rewrote in place", () => {
        const onValueChange = vi.fn();
        render(
          <ControlledTagsInput
            initial={["Alpha", "Beta", "Gamma"]}
            reorderable
            onValueChange={onValueChange}
          />,
        );

        fireEvent.click(screen.getByLabelText("Edit Beta"));
        const editInput = screen.getByDisplayValue("Beta");
        fireEvent.change(editInput, { target: { value: "Beta Fixed" } });
        fireEvent.keyDown(editInput, { key: "Enter" });
        expect(screen.getByLabelText("Reorder: Beta Fixed")).toBeInTheDocument();

        dragChip(["Alpha", "Beta Fixed", "Gamma"], "Beta Fixed", "Alpha", 30);

        expect(onValueChange).toHaveBeenLastCalledWith(["Beta Fixed", "Alpha", "Gamma"]);
      });
    });

    describe("mid-drag value changes are suppressed (regression: stale drag state)", () => {
      // The sync effect remaps the entry list whenever the external `value` changes. If that
      // happens while formkit has an active drag, the drag's `draggedNode` can point at a
      // missing entry and performing the sort would splice the OLD value back into the list by
      // value - resurrecting a value that was just removed, or emitting an order built from a
      // mix of pre- and post-change entries. The component cancels the whole in-flight drag in
      // that case: setValues refuses the order formkit computes and onSort stays silent, so the
      // external `value` is exactly what the remove/edit/add produced.
      it("does not resurrect a value removed from the list while its chip is being dragged", () => {
        const onValueChange = vi.fn();
        render(
          <ControlledTagsInput
            initial={["Alpha", "Beta", "Gamma"]}
            reorderable
            onValueChange={onValueChange}
          />,
        );

        const handle = screen.getByLabelText("Reorder: Beta");
        const betaNode = chipNodeFor("Reorder: Beta");
        fireEvent(handle, new PointerEvent("pointerdown", { bubbles: true }));
        fireEvent(betaNode, new MouseEvent("dragstart", { bubbles: true }));

        fireEvent.click(screen.getByLabelText("Remove Beta"));
        expect(onValueChange).toHaveBeenLastCalledWith(["Alpha", "Gamma"]);

        const gammaNode = chipNodeFor("Reorder: Gamma");
        setChipRect(gammaNode, 100);
        fireEvent(
          gammaNode,
          new MouseEvent("dragover", { clientX: 150, clientY: 5, bubbles: true }),
        );
        fireEvent(gammaNode, new MouseEvent("drop", { bubbles: true }));
        fireEvent(gammaNode, new MouseEvent("dragend", { bubbles: true }));

        // The remove was the only emission; completing the drag did not reorder anything and
        // did not bring "Beta" back.
        expect(onValueChange).toHaveBeenCalledTimes(1);
        expect(onValueChange).toHaveBeenLastCalledWith(["Alpha", "Gamma"]);
        expect(screen.queryByText("Beta")).not.toBeInTheDocument();
      });

      it("does not apply a reorder when a new chip was added while the drag was in flight", () => {
        const onValueChange = vi.fn();
        render(
          <ControlledTagsInput
            initial={["Alpha", "Beta", "Gamma"]}
            reorderable
            onValueChange={onValueChange}
          />,
        );

        const handle = screen.getByLabelText("Reorder: Alpha");
        const alphaNode = chipNodeFor("Reorder: Alpha");
        fireEvent(handle, new PointerEvent("pointerdown", { bubbles: true }));
        fireEvent(alphaNode, new MouseEvent("dragstart", { bubbles: true }));

        const input = screen.getByRole("textbox");
        fireEvent.change(input, { target: { value: "Delta" } });
        fireEvent.keyDown(input, { key: "Enter" });
        expect(onValueChange).toHaveBeenLastCalledWith(["Alpha", "Beta", "Gamma", "Delta"]);
        expect(screen.getByLabelText("Reorder: Delta")).toBeInTheDocument();

        const alphaNodeAfter = chipNodeFor("Reorder: Alpha");
        const gammaNode = chipNodeFor("Reorder: Gamma");
        setChipRect(alphaNodeAfter, 0);
        setChipRect(gammaNode, 200);
        fireEvent(
          gammaNode,
          new MouseEvent("dragover", { clientX: 250, clientY: 5, bubbles: true }),
        );
        fireEvent(gammaNode, new MouseEvent("drop", { bubbles: true }));
        fireEvent(gammaNode, new MouseEvent("dragend", { bubbles: true }));

        // The add was the only emission; the dropped drag did not reorder the post-add list.
        expect(onValueChange).toHaveBeenCalledTimes(1);
        expect(onValueChange).toHaveBeenLastCalledWith(["Alpha", "Beta", "Gamma", "Delta"]);
      });

      it("does not emit a stale order when the chip being dragged is edited mid-drag", () => {
        const onValueChange = vi.fn();
        render(
          <ControlledTagsInput
            initial={["Alpha", "Beta", "Gamma"]}
            reorderable
            onValueChange={onValueChange}
          />,
        );

        const handle = screen.getByLabelText("Reorder: Beta");
        const betaNode = chipNodeFor("Reorder: Beta");
        fireEvent(handle, new PointerEvent("pointerdown", { bubbles: true }));
        fireEvent(betaNode, new MouseEvent("dragstart", { bubbles: true }));

        fireEvent.click(screen.getByLabelText("Edit Beta"));
        const editInput = screen.getByDisplayValue("Beta");
        fireEvent.change(editInput, { target: { value: "Beta Fixed" } });
        fireEvent.keyDown(editInput, { key: "Enter" });
        expect(onValueChange).toHaveBeenLastCalledWith(["Alpha", "Beta Fixed", "Gamma"]);
        expect(screen.getByLabelText("Reorder: Beta Fixed")).toBeInTheDocument();

        const betaFixedNode = chipNodeFor("Reorder: Beta Fixed");
        const gammaNode = chipNodeFor("Reorder: Gamma");
        setChipRect(betaFixedNode, 100);
        setChipRect(gammaNode, 200);
        fireEvent(
          gammaNode,
          new MouseEvent("dragover", { clientX: 250, clientY: 5, bubbles: true }),
        );
        fireEvent(gammaNode, new MouseEvent("drop", { bubbles: true }));
        fireEvent(gammaNode, new MouseEvent("dragend", { bubbles: true }));

        // The edit was the only emission; "Beta" never comes back and no stale order is sent.
        expect(onValueChange).toHaveBeenCalledTimes(1);
        expect(onValueChange).toHaveBeenLastCalledWith(["Alpha", "Beta Fixed", "Gamma"]);
        expect(screen.queryByText("Beta")).not.toBeInTheDocument();
      });

      it("allows a fresh drag after a mid-drag removal (cancellation is not sticky)", () => {
        const onValueChange = vi.fn();
        render(
          <ControlledTagsInput
            initial={["Alpha", "Beta", "Gamma", "Delta"]}
            reorderable
            onValueChange={onValueChange}
          />,
        );

        const handle = screen.getByLabelText("Reorder: Beta");
        const betaNode = chipNodeFor("Reorder: Beta");
        fireEvent(handle, new PointerEvent("pointerdown", { bubbles: true }));
        fireEvent(betaNode, new MouseEvent("dragstart", { bubbles: true }));
        fireEvent.click(screen.getByLabelText("Remove Beta"));
        const gammaNode = chipNodeFor("Reorder: Gamma");
        setChipRect(gammaNode, 100);
        fireEvent(
          gammaNode,
          new MouseEvent("dragover", { clientX: 150, clientY: 5, bubbles: true }),
        );
        fireEvent(gammaNode, new MouseEvent("drop", { bubbles: true }));
        fireEvent(gammaNode, new MouseEvent("dragend", { bubbles: true }));
        expect(onValueChange).toHaveBeenCalledTimes(1);

        // The onDragend clear means the next drag works normally against the shrunken list.
        dragChip(["Alpha", "Gamma", "Delta"], "Gamma", "Delta", 250);

        expect(onValueChange).toHaveBeenLastCalledWith(["Alpha", "Delta", "Gamma"]);
      });
    });

    describe("disabled drag attempts", () => {
      it("cannot start a drag when `disabled` is set (no handle is even rendered)", () => {
        const onValueChange = vi.fn();
        render(
          <ControlledTagsInput
            initial={["Alpha", "Beta"]}
            reorderable
            disabled
            onValueChange={onValueChange}
          />,
        );

        const fromNode = chipNodeFor("Edit Alpha");
        const toNode = chipNodeFor("Edit Beta");
        setChipRect(fromNode, 0);
        setChipRect(toNode, 100);
        fireEvent(fromNode, new PointerEvent("pointerdown", { bubbles: true }));
        fireEvent(fromNode, new MouseEvent("dragstart", { bubbles: true }));
        fireEvent(toNode, new MouseEvent("dragover", { clientX: 150, clientY: 5, bubbles: true }));

        expect(onValueChange).not.toHaveBeenCalled();
      });

      it("cannot start a drag when reorderable is false", () => {
        const onValueChange = vi.fn();
        render(<ControlledTagsInput initial={["Alpha", "Beta"]} onValueChange={onValueChange} />);

        const fromNode = chipNodeFor("Edit Alpha");
        const toNode = chipNodeFor("Edit Beta");
        setChipRect(fromNode, 0);
        setChipRect(toNode, 100);
        fireEvent(fromNode, new PointerEvent("pointerdown", { bubbles: true }));
        fireEvent(fromNode, new MouseEvent("dragstart", { bubbles: true }));
        fireEvent(toNode, new MouseEvent("dragover", { clientX: 150, clientY: 5, bubbles: true }));

        expect(onValueChange).not.toHaveBeenCalled();
      });

      it("does not confuse a plain click on the chip with a drag", () => {
        const onValueChange = vi.fn();
        render(
          <ControlledTagsInput
            initial={["Alpha", "Beta"]}
            reorderable
            onValueChange={onValueChange}
          />,
        );

        fireEvent.click(screen.getByLabelText("Edit Alpha"));

        expect(onValueChange).not.toHaveBeenCalled();
      });
    });
  });

  describe("duplicate values (defense in depth)", () => {
    // TagsInput itself never creates a duplicate through typing or in-place editing, but a
    // caller can still pass in a `value` array containing one - it happened via BookEditForm's
    // "similar existing value" hint. Chips must stay keyed/identified by position, not by their
    // (possibly non-unique) string value, or React and the drag library both get confused about which
    // element is which.
    it("does not trigger a React duplicate-key warning when two chips share a value (reorderable)", () => {
      const errorSpy = vi.spyOn(console, "error").mockImplementation(() => {});

      render(<ControlledTagsInput initial={["Same", "Same"]} reorderable />);

      const duplicateKeyWarning = errorSpy.mock.calls.some((args) =>
        String(args[0]).toLowerCase().includes("same key"),
      );
      expect(duplicateKeyWarning).toBe(false);

      errorSpy.mockRestore();
    });

    it("removes exactly the clicked duplicate, not both, when two chips share a value", () => {
      const onValueChange = vi.fn();
      render(
        <ControlledTagsInput
          initial={["Duplicate Name", "Duplicate Name"]}
          onValueChange={onValueChange}
        />,
      );

      const removeButtons = screen.getAllByLabelText("Remove Duplicate Name");
      expect(removeButtons).toHaveLength(2);

      fireEvent.click(removeButtons[0]!);

      expect(onValueChange).toHaveBeenCalledWith(["Duplicate Name"]);
    });

    it("keeps both duplicates through a reorder (formkit sorts by entry identity, not by string)", () => {
      const onValueChange = vi.fn();
      render(
        <ControlledTagsInput
          initial={["Same", "Same", "Gamma"]}
          reorderable
          onValueChange={onValueChange}
        />,
      );

      const firstHandle = screen.getAllByLabelText("Reorder: Same")[0]!;
      const fromNode = firstHandle.parentElement!;
      const toNode = chipNodeFor("Reorder: Gamma");
      setChipRect(fromNode, 0);
      setChipRect(toNode, 200);
      fireEvent(firstHandle, new PointerEvent("pointerdown", { bubbles: true }));
      fireEvent(fromNode, new MouseEvent("dragstart", { bubbles: true }));
      fireEvent(toNode, new MouseEvent("dragover", { clientX: 250, clientY: 5, bubbles: true }));
      fireEvent(toNode, new MouseEvent("drop", { bubbles: true }));
      fireEvent(toNode, new MouseEvent("dragend", { bubbles: true }));

      // The dragged chip moved to the end; its twin is untouched - the list still has two
      // "Same" entries (formkit's value-equality sort would have dropped one).
      expect(onValueChange).toHaveBeenCalledTimes(1);
      expect(onValueChange).toHaveBeenCalledWith(["Same", "Gamma", "Same"]);
      expect(screen.getAllByLabelText("Reorder: Same")).toHaveLength(2);
    });

    it("sorting a unique chip over the duplicate pair keeps both twins", () => {
      const onValueChange = vi.fn();
      render(
        <ControlledTagsInput
          initial={["Alpha", "Same", "Same"]}
          reorderable
          onValueChange={onValueChange}
        />,
      );

      const alphaNode = chipNodeFor("Reorder: Alpha");
      const secondSameNode = screen.getAllByLabelText("Reorder: Same")[1]!.parentElement!;
      setChipRect(alphaNode, 0);
      setChipRect(secondSameNode, 200);

      fireEvent(
        screen.getByLabelText("Reorder: Alpha"),
        new PointerEvent("pointerdown", { bubbles: true }),
      );
      fireEvent(alphaNode, new MouseEvent("dragstart", { bubbles: true }));
      fireEvent(
        secondSameNode,
        new MouseEvent("dragover", { clientX: 250, clientY: 5, bubbles: true }),
      );
      fireEvent(secondSameNode, new MouseEvent("drop", { bubbles: true }));
      fireEvent(secondSameNode, new MouseEvent("dragend", { bubbles: true }));

      expect(onValueChange).toHaveBeenCalledTimes(1);
      expect(onValueChange).toHaveBeenCalledWith(["Same", "Same", "Alpha"]);
      expect(screen.getAllByLabelText("Reorder: Same")).toHaveLength(2);
    });
  });
});
