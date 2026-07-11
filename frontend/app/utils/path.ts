export function getLeafDirectoryName(fullPath: string): string {
    // Normalize the path by removing a trailing slash/backslash.
    let normalizedPath = fullPath.replace(/[/\\]$/, '');

    // Find the index of the last separator.
    const lastSlash = normalizedPath.lastIndexOf('/');
    const lastBackslash = normalizedPath.lastIndexOf('\\');
    const lastSeparatorIndex = Math.max(lastSlash, lastBackslash);

    // Extract the final component.
    // Start the substring *after* the last separator.
    const leafName = normalizedPath.substring(lastSeparatorIndex + 1);

    // If the result is empty, it means the path was a root (e.g., '/', 'C:').
    if (leafName.length === 0) {
        // Return the root component itself (e.g., '/')
        return normalizedPath;
    }

    return leafName;
}

/** Explore link for a completed history item's content folder, or null when unavailable. */
export function getExploreContentLink(storage: string | null | undefined, category: string | null | undefined): string | null {
    if (!storage || !category) return null;
    const downloadFolder = getLeafDirectoryName(storage);
    if (!downloadFolder) return null;
    return `/explore/content/${encodeURIComponent(category)}/${encodeURIComponent(downloadFolder)}`;
}