interface PreparePreorderNavigationOptions {
  persistCurrentOwnerJournal: () => boolean
  saveAndDrainRemote: () => Promise<boolean>
}

export function markPreorderContextDiscarded(discardedContexts: Set<string>, contextKey: string) {
  discardedContexts.add(contextKey)
}

export function consumePreorderContextPersistence(discardedContexts: Set<string>, contextKey: string) {
  return !discardedContexts.delete(contextKey)
}

export async function preparePreorderNavigation(options: PreparePreorderNavigationOptions) {
  const journalSaved = options.persistCurrentOwnerJournal()
  let serverSaved = false
  try {
    serverSaved = await options.saveAndDrainRemote()
  } catch {
    serverSaved = false
  }
  if (serverSaved) return { canLeave: true, protectedBy: 'server' as const }
  if (journalSaved) return { canLeave: true, protectedBy: 'journal' as const }
  return { canLeave: false, protectedBy: null }
}
