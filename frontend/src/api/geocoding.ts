// Address ↔ coordinates via Nominatim (OpenStreetMap's free geocoder).
// Usage policy: max 1 request per second, no search-as-you-type — so we only search when the user presses
// Enter / the search button. Note: the typed address is sent to this external service.
// https://operations.osmfoundation.org/policies/nominatim/

import type { Place } from './places'

const NOMINATIM = 'https://nominatim.openstreetmap.org'

// Prefer results around Eindhoven (not a hard limit): lon/lat of the west, north, east, south edges.
const EINDHOVEN_VIEWBOX = '5.30,51.52,5.60,51.38'

interface NominatimResult {
  lat: string
  lon: string
  name?: string
  display_name: string
  address?: Record<string, string>
}

/** Short, readable label: "Name, Street 12, City" instead of the full administrative chain. */
function label(r: NominatimResult): string {
  const a = r.address ?? {}
  const street = [a.road, a.house_number].filter(Boolean).join(' ')
  const city = a.city ?? a.town ?? a.village ?? a.suburb
  const parts = [r.name, street, city].filter((p, i, all) => p && all.indexOf(p) === i)
  return (parts.length ? parts.join(', ') : r.display_name).slice(0, 200)
}

export async function searchAddress(query: string): Promise<Place[]> {
  const params = new URLSearchParams({
    q: query, format: 'jsonv2', addressdetails: '1', limit: '5',
    countrycodes: 'nl', viewbox: EINDHOVEN_VIEWBOX, 'accept-language': 'en',
  })
  const response = await fetch(`${NOMINATIM}/search?${params}`)
  if (!response.ok) throw new Error('Address search is not available right now.')
  const results = (await response.json()) as NominatimResult[]
  return results.map((r) => ({ name: label(r), lat: Number(r.lat), lng: Number(r.lon) }))
}

/** Name for a point clicked on the map; falls back to the coordinates if the lookup fails. */
export async function reverseGeocode(lat: number, lng: number): Promise<string> {
  const fallback = `${lat.toFixed(5)}, ${lng.toFixed(5)}`
  try {
    const params = new URLSearchParams({ lat: String(lat), lon: String(lng), format: 'jsonv2', addressdetails: '1', zoom: '18', 'accept-language': 'en' })
    const response = await fetch(`${NOMINATIM}/reverse?${params}`)
    if (!response.ok) return fallback
    const result = (await response.json()) as NominatimResult
    return result?.display_name ? label(result) : fallback
  } catch {
    return fallback
  }
}
