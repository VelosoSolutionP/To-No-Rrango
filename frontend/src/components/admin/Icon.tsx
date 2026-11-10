const paths: Record<string, string> = {
  overview: "M4 13h6V4H4v9Zm0 7h6v-4H4v4Zm10 0h6v-9h-6v9Zm0-16v4h6V4h-6Z",
  orders: "M6 3h12l2 4v13a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V7l2-4Zm-2 4h16M9 11h6M9 15h6",
  kitchen: "M7 3v8m-3-8v5a3 3 0 0 0 6 0V3M7 11v10M17 3c-2 1-3 4-3 7 0 2 1 3 3 3v8",
  menu: "M5 4h11a3 3 0 0 1 3 3v13H8a3 3 0 0 1-3-3V4Zm0 13a3 3 0 0 1 3-3h11M9 8h6",
  inventory: "M12 3 4 7v10l8 4 8-4V7l-8-4Zm0 9 8-5M12 12 4 7m8 5v9",
  parties: "M16 20v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2M9 10a4 4 0 1 0 0-8 4 4 0 0 0 0 8Zm13 10v-2a4 4 0 0 0-3-3.9M16 2.1a4 4 0 0 1 0 7.8",
  finance: "M3 7h18v13H3V7Zm0 4h18M16 15h2M7 7V4h10v3",
  reports: "M4 20V10m6 10V4m6 16v-7m6 7H2",
  customers: "M16 20v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2M9 10a4 4 0 1 0 0-8 4 4 0 0 0 0 8Zm13 10v-2a4 4 0 0 0-3-3.9M16 2.1a4 4 0 0 1 0 7.8",
  distribution: "M3 6h11v11H3V6Zm11 4h4l3 3v4h-7v-7ZM7.5 20a2 2 0 1 0 0-4 2 2 0 0 0 0 4Zm10 0a2 2 0 1 0 0-4 2 2 0 0 0 0 4Z",
  fiscal: "M6 2h9l4 4v16H6V2Zm9 0v4h4M9 12h7M9 16h7",
  whatsapp: "M21 12a9 9 0 0 1-13.3 7.9L3 21l1.2-4.6A9 9 0 1 1 21 12ZM9 9c0 3.3 2.7 6 6 6",
  zest: "M13 2 4 14h7l-1 8 9-12h-7l1-8Z",
  settings: "M4 7h10m4 0h2M4 17h2m4 0h10M14 4v6m-4 4v6",
  establishment: "M4 10v10h16V10M3 10l2-6h14l2 6M3 10a3 3 0 0 0 6 0 3 3 0 0 0 6 0 3 3 0 0 0 6 0M10 20v-5h4v5",
  edit: "M4 20h4L19 9l-4-4L4 16v4Zm10-14 4 4",
  trash: "M4 7h16M10 11v6m4-6v6M6 7l1 13h10l1-13M9 7V4h6v3",
  eye: "M2 12s4-7 10-7 10 7 10 7-4 7-10 7S2 12 2 12Zm10 3a3 3 0 1 0 0-6 3 3 0 0 0 0 6Z",
  power: "M12 3v9m6.4-5.4a8 8 0 1 1-12.8 0",
  plus: "M12 5v14M5 12h14",
  search: "M11 18a7 7 0 1 0 0-14 7 7 0 0 0 0 14Zm10 3-5-5",
  check: "M5 12l5 5 9-10",
  undo: "M9 14 4 9l5-5M4 9h11a5 5 0 0 1 0 10h-3",
  whatsappSend: "M22 2 11 13M22 2l-7 20-4-9-9-4 20-7Z",
  lock: "M6 11h12v10H6V11Zm2 0V8a4 4 0 0 1 8 0v3",
  download: "M12 3v12m-5-5 5 5 5-5M4 21h16",
  box: "M12 3 4 7v10l8 4 8-4V7l-8-4Z",
  pos: "M4 4h16v10H4V4Zm4 16h8m-4-6v6M8 8h3m3 0h2",
  truck: "M3 6h11v11H3V6Zm11 4h4l3 3v4h-7v-7ZM7.5 20a2 2 0 1 0 0-4 2 2 0 0 0 0 4Zm10 0a2 2 0 1 0 0-4 2 2 0 0 0 0 4Z",
  tag: "M3 12V4h8l10 10-8 8L3 12Zm5-4h.01",
  pause: "M8 5v14m8-14v14",
  play: "M7 5l12 7-12 7V5Z",
};

export function Icon({ name, size = 20 }: { name: string; size?: number }) {
  return (
    <svg aria-hidden="true" fill="none" height={size} stroke="currentColor" strokeLinecap="round" strokeLinejoin="round" strokeWidth="1.7" viewBox="0 0 24 24" width={size}>
      <path d={paths[name] ?? paths.overview} />
    </svg>
  );
}
