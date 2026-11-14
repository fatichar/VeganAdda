// The offline page is served in place of the requested URL, so reloading retries that page.
addEventListener('online', () => location.reload());
