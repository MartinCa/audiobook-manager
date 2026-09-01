/**
 * openapi-typescript renders every DTO property as optional and nullable, because Swashbuckle
 * only emits an OpenAPI `required` array for types carrying explicit `[Required]` attributes
 * (mainly this API's request DTOs). This backend's response DTOs are plain C# records, whose
 * non-nullable positional properties get no such annotation, so `api-types.ts` alone can't tell
 * you which fields the server actually always sends.
 *
 * `Require<Dto, "id" | "bookName">` restores that guarantee for the fields the DTO's C# source
 * confirms are non-nullable, so callers don't have to `?.`/`!` fields the server always sends.
 * Each `src/types/*.ts` file that uses this cites the C# DTO its required-key list was checked
 * against — recheck it there before widening the list.
 */
export type Require<T, K extends keyof T> = Omit<T, K> & { [P in K]-?: NonNullable<T[P]> };
