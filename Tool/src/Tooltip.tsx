import { HelpCircle } from 'lucide-react'
import { useState } from 'react'

export function Tooltip({ text }: { text: string }) {
  const [show, setShow] = useState(false)

  return (
    <span
      className="relative inline-block ml-1 align-middle"
      onMouseEnter={() => setShow(true)}
      onMouseLeave={() => setShow(false)}
    >
      <HelpCircle size={14} className="text-tarkov-text-dim hover:text-tarkov-accent cursor-help" />
      {show && (
        <span className="absolute bottom-full left-1/2 -translate-x-1/2 mb-2 w-56 px-2 py-1.5 text-xs text-tarkov-text bg-tarkov-bg border border-tarkov-border rounded shadow-lg z-50">
          {text}
        </span>
      )}
    </span>
  )
}
