import fs from 'fs'
import path from 'path'
import { fileURLToPath } from 'url'
import readline from 'readline'

const __dirname = path.dirname(fileURLToPath(import.meta.url))
const locationsDir = path.join(__dirname, '..', '..', 'locations')
const sourceDir = process.env.SOURCE_DIR || 'D:\\SPT MODS\\SPT BUNDLES\\ExportedProject\\Assets\\Content\\Locations'

const folderToMapIds = {
  'City': ['tarkovstreets'],
  'Custom': ['bigmap'],
  'Factory': ['factory4_day', 'factory4_night'],
  'Factory_Rework': ['factory4_day', 'factory4_night'],
  'Laboratory': ['laboratory'],
  'Labyrinth': ['labyrinth'],
  'Lighthouse': ['lighthouse'],
  'Reserve_Base': ['rezervbase'],
  'Sandbox': ['sandbox', 'sandbox_high'],
  'Shopping_Mall': ['interchange'],
  'shorline': ['shoreline'],
  'Woods': ['woods']
}

const posRe = /^    _pos:\s*\{x:\s*([-0-9.]+),\s*y:\s*([-0-9.]+),\s*z:\s*([-0-9.]+)\}/
const nameRe = /^    _name:\s*(.+)$/
const itemStartRe = /^  - _id:/
const sectionKeyRe = /^  [A-Za-z]/

async function extractFromFile(filePath) {
  const stream = fs.createReadStream(filePath)
  const rl = readline.createInterface({ input: stream, crlfDelay: Infinity })

  const entries = []
  let inSection = false
  let current = null

  for await (const raw of rl) {
    const line = raw.trimEnd()

    if (inSection) {
      if (line.length === 0) continue
      if (itemStartRe.test(line)) {
        if (current && current.name != null) entries.push(current)
        current = { position: null, name: null }
        continue
      }
      if (!line.startsWith('    ')) {
        if (current && current.name != null) entries.push(current)
        inSection = false
        current = null
        if (line === '  ExfiltrationPoints:') inSection = true
        continue
      }
      const posMatch = posRe.exec(line)
      if (posMatch) {
        current ??= { position: null, name: null }
        current.position = {
          x: parseFloat(posMatch[1]),
          y: parseFloat(posMatch[2]),
          z: parseFloat(posMatch[3])
        }
        continue
      }
      const nameMatch = nameRe.exec(line)
      if (nameMatch && current) {
        current.name = nameMatch[1].trim()
        continue
      }
    } else if (line === '  ExfiltrationPoints:') {
      inSection = true
      current = { position: null, name: null }
    }
  }

  if (current && current.name != null) entries.push(current)
  rl.close()
  return entries
}

async function main() {
  const stats = { processed: 0, skipped: 0 }
  const perMap = new Map()

  for (const [folder, mapIds] of Object.entries(folderToMapIds)) {
    const dir = path.join(sourceDir, folder)
    if (!fs.existsSync(dir)) {
      console.log(`[skip] ${folder}: location folder not found`)
      stats.skipped++
      continue
    }
    const files = fs.readdirSync(dir).filter(f => f.toLowerCase().endsWith('_ai.unity'))
    if (files.length === 0) {
      console.log(`[skip] ${folder}: AI scene not found`)
      stats.skipped++
      continue
    }
    const targetFile = path.join(dir, files[0])

    const entries = await extractFromFile(targetFile)
    if (entries.length === 0) {
      console.log(`[skip] ${folder}: no ExfiltrationPoints found`)
      stats.skipped++
      continue
    }

    for (const mapId of mapIds) {
      const list = perMap.get(mapId) || []
      for (const e of entries) {
        if (!list.find(x => x.name === e.name))
          list.push(e)
      }
      perMap.set(mapId, list)
    }
    stats.processed++
  }

  let written = 0
  for (const [mapId, entries] of perMap) {
    const outDir = path.join(locationsDir, mapId)
    fs.mkdirSync(outDir, { recursive: true })
    const outPath = path.join(outDir, 'extractPositions.json')
    fs.writeFileSync(outPath, JSON.stringify(entries, null, 2))
    console.log(`[write] ${outPath} (${entries.length} extracts)`)
    written++
  }

  console.log('Done:', { processed: stats.processed, written, skipped: stats.skipped })
}

main().catch(err => {
  console.error(err)
  process.exit(1)
})
