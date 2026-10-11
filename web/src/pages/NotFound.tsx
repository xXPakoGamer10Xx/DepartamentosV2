import { Link } from 'react-router-dom'

export default function NotFound() {
  return (
    <div className="grid h-full place-items-center bg-bg p-6 text-center">
      <div>
        <p className="text-6xl font-extrabold text-primary">404</p>
        <p className="mt-2 text-fg-2">Esta página no existe.</p>
        <Link to="/" className="mt-5 inline-block rounded-xl bg-primary px-5 py-2.5 text-sm font-semibold text-white">Ir al inicio</Link>
      </div>
    </div>
  )
}
