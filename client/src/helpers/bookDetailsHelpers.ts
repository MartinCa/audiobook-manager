import type { AudiobookPerson } from "../types/Audiobook";

export const joinPersons = (persons?: AudiobookPerson[] | null): string =>
  persons && persons.length > 0 ? persons.map((p) => p.name).join(", ") : "";
