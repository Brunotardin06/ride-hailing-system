import { Link } from 'react-router-dom'
import EmptyState from '../components/ui/EmptyState'

export default function NotFoundPage() {
  return (
    <section className="card">
      <EmptyState icon="pin" title="Page not found" message="This route does not exist." />
      <p className="center">
        <Link to="/" className="button">
          Back to home
        </Link>
      </p>
    </section>
  )
}
