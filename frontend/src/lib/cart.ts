import type { MenuItem, MenuOption } from "./types";

export interface CartOption {
  groupName: string;
  option: MenuOption;
}

export interface CartLine {
  key: string;
  item: MenuItem;
  quantity: number;
  notes: string;
  options: CartOption[];
}

export function cartLineKey(itemId: string, optionIds: string[]): string {
  return `${itemId}:${[...optionIds].sort().join(",")}`;
}

export function cartLineUnitPrice(line: CartLine): number {
  return (
    line.item.priceInCents +
    line.options.reduce((total, selected) => total + selected.option.priceInCents, 0)
  );
}

export function cartLineTotal(line: CartLine): number {
  return cartLineUnitPrice(line) * line.quantity;
}

export function cartSubtotal(lines: CartLine[]): number {
  return lines.reduce((total, line) => total + cartLineTotal(line), 0);
}

export function cartCount(lines: CartLine[]): number {
  return lines.reduce((total, line) => total + line.quantity, 0);
}
