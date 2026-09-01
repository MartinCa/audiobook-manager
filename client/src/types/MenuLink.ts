export interface MenuLink {
  to?: string;
  icon: string;
  text: string;
  subLinks?: MenuLink[];
}
