export default function Legend({ items }: { items: { label: string; color: string }[] }) {
  return (
    <ul className="legend">
      {items.map((item) => (
        <li key={item.label}>
          <span className="legend-swatch" style={{ background: item.color }} />
          {item.label}
        </li>
      ))}
    </ul>
  )
}
