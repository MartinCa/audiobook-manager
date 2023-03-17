export interface PaginatedResult<T> {
  count: number;
  total: number;
  items: T[];
}
