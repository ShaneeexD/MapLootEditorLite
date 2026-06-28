import fs from 'fs'
import path from 'path'
import { fileURLToPath } from 'url'

const __dirname = path.dirname(fileURLToPath(import.meta.url))
const sptPath = process.env.SPT_PATH || 'C:\\SPT'
const manifestPath = path.join(sptPath, 'EscapeFromTarkov_Data', 'StreamingAssets', 'Windows', 'Windows.json')
const outputPath = path.join(__dirname, '..', 'public', 'bundles.json')

if (!fs.existsSync(manifestPath)) {
  console.error('Manifest not found:', manifestPath)
  process.exit(1)
}

console.log('Reading manifest from', manifestPath)
const json = fs.readFileSync(manifestPath, 'utf-8')
const manifest = JSON.parse(json)

const bundles = Object.keys(manifest)
  .map((p) => {
    const parts = p.split('/')
    return { path: p, name: parts[parts.length - 1] || p }
  })
  .sort((a, b) => a.path.localeCompare(b.path))

const publicDir = path.dirname(outputPath)
if (!fs.existsSync(publicDir)) {
  fs.mkdirSync(publicDir, { recursive: true })
}

fs.writeFileSync(outputPath, JSON.stringify(bundles))
console.log(`Wrote ${bundles.length} bundles to ${outputPath}`)
