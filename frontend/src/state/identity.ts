import { useState } from 'react'

// There is no authentication in this MVP: a tab simply chooses who it is.
// Passengers are not stored by any service, so they are fixed demo identities here.
export const demoPassengers = [
  { id: '22222222-2222-2222-2222-000000000001', name: 'Alice Jansen' },
  { id: '22222222-2222-2222-2222-000000000002', name: 'Bram de Boer' },
  { id: '22222222-2222-2222-2222-000000000003', name: 'Chloé Martin' },
]

export const defaultDriverId = '11111111-1111-1111-1111-000000000001' // Emma, first to receive offers

/**
 * State kept in sessionStorage, i.e. per browser tab. Two tabs can therefore be two different
 * people (e.g. one passenger and one driver, or two drivers) at the same time.
 */
export function useTabState(key: string, initial: string): [string, (value: string) => void] {
  const [value, setValue] = useState(() => sessionStorage.getItem(key) ?? initial)
  const update = (next: string) => {
    sessionStorage.setItem(key, next)
    setValue(next)
  }
  return [value, update]
}
