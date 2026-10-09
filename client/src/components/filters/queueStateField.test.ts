import { describe, it, expect } from "vitest";
import { activeChips } from "./filterUtils";
import { queueStateField } from "./queueStateField";

describe("queueStateField", () => {
  it("builds the Queue status multiselect from the served options, values and wording", () => {
    const field = queueStateField([
      { value: "NotQueued", label: "Not in any queue" },
      { value: "MatchRejected", label: "Online match rejected" },
    ]);

    expect(field).toEqual({
      type: "multiselect",
      key: "queueStates",
      label: "Queue status",
      options: ["NotQueued", "MatchRejected"],
      optionLabels: { NotQueued: "Not in any queue", MatchRejected: "Online match rejected" },
    });
  });

  it("has no options until the served list arrives, without throwing", () => {
    expect(queueStateField(undefined)).toMatchObject({ key: "queueStates", options: [] });
  });

  it("still describes a value already in the filters before the options have loaded", () => {
    const chips = activeChips([queueStateField(undefined)], { queueStates: ["NotQueued"] });

    expect(chips.map((chip) => chip.label)).toEqual(["Queue status: NotQueued"]);
  });
});
