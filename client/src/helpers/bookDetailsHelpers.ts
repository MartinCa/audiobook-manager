import { AudiobookPerson } from "../types/Audiobook";

export const joinPersons = (persons: AudiobookPerson[]) =>
  persons.map((p) => p.name).join(", ");
