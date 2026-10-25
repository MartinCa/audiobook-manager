import { describe, it, expect } from "vitest";
import { moveItem, orderLikeServices, sortServicesByOrder } from "./metadataSources";

describe("orderLikeServices", () => {
  const services = [{ name: "Hardcover" }, { name: "Audible" }, { name: "Goodreads" }];

  it("orders the names like the served services, whatever order they were selected in", () => {
    expect(orderLikeServices(["Goodreads", "Audible", "Hardcover"], services)).toEqual([
      "Hardcover",
      "Audible",
      "Goodreads",
    ]);
  });

  it("keeps a name the services do not know, after the known ones and in its given order", () => {
    expect(orderLikeServices(["Zed", "Audible", "Alpha"], services)).toEqual([
      "Audible",
      "Zed",
      "Alpha",
    ]);
  });
});

describe("moveItem", () => {
  it("moves an item up and down by one", () => {
    expect(moveItem(["a", "b", "c"], 1, -1)).toEqual(["b", "a", "c"]);
    expect(moveItem(["a", "b", "c"], 1, 1)).toEqual(["a", "c", "b"]);
  });

  it("leaves the list alone when the move would leave it", () => {
    expect(moveItem(["a", "b"], 0, -1)).toEqual(["a", "b"]);
    expect(moveItem(["a", "b"], 1, 1)).toEqual(["a", "b"]);
  });

  it("does not mutate its input", () => {
    const input = ["a", "b"];
    moveItem(input, 0, 1);
    expect(input).toEqual(["a", "b"]);
  });
});

describe("sortServicesByOrder", () => {
  const services = [{ name: "Audible" }, { name: "Goodreads" }, { name: "Hardcover" }];

  it("puts listed sources first in the saved order, then the rest alphabetically", () => {
    expect(sortServicesByOrder(services, ["hardcover", "Goodreads"]).map((s) => s.name)).toEqual([
      "Hardcover",
      "Goodreads",
      "Audible",
    ]);
  });
});
