// Minimal fetch wrapper: JSON in/out, and readable errors from ASP.NET Core ProblemDetails responses.

export class ApiError extends Error {
  readonly status: number

  constructor(status: number, message: string) {
    super(message)
    this.status = status
  }
}

export async function request<T>(baseUrl: string, path: string, init: RequestInit = {}): Promise<T> {
  let response: Response
  try {
    response = await fetch(baseUrl + path, {
      ...init,
      headers: { 'Content-Type': 'application/json', ...init.headers },
    })
  } catch {
    throw new ApiError(0, `Cannot reach ${baseUrl}. Is the service running?`)
  }

  if (!response.ok) {
    let message = `${response.status} ${response.statusText}`
    try {
      const problem = await response.json()
      message = problem.detail ?? problem.title ?? message
      if (problem.errors) message += ': ' + Object.values(problem.errors).flat().join(' ')
    } catch {
      // not JSON
    }
    throw new ApiError(response.status, message)
  }

  if (response.status === 204 || response.headers.get('content-length') === '0') return undefined as T
  const text = await response.text()
  return (text ? JSON.parse(text) : undefined) as T
}

export const json = (body: unknown): RequestInit => ({ method: 'POST', body: JSON.stringify(body) })
