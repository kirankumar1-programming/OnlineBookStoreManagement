const CACHE_NAME = 'bookstore-cache-v1';
const STATIC_ASSETS = [
    '/',
    '/offline.html',
    '/css/site.css',
    '/css/chatbot.css',
    '/js/site.js',
    '/js/chatbot.js',
    '/js/offline-store.js',
    '/js/offline-sync.js',
    '/manifest.json',
    '/images/default-book.svg',
    '/images/default-book.png',
    '/favicon.ico',
    'https://cdn.jsdelivr.net/npm/bootstrap@5.3.2/dist/css/bootstrap.min.css',
    'https://cdn.jsdelivr.net/npm/bootstrap-icons@1.11.1/font/bootstrap-icons.css',
    'https://cdn.jsdelivr.net/npm/bootstrap@5.3.2/dist/js/bootstrap.bundle.min.js',
    'https://code.jquery.com/jquery-3.7.1.min.js'
];

// Install Event: Precache Static Shell Assets
self.addEventListener('install', (event) => {
    event.waitUntil(
        caches.open(CACHE_NAME).then((cache) => {
            return Promise.allSettled(
                STATIC_ASSETS.map((url) =>
                    fetch(url, { mode: 'cors' })
                        .then((res) => {
                            if (res.ok) return cache.put(url, res);
                        })
                        .catch((err) => {
                            console.warn('[SW] Failed to precache:', url, err);
                        })
                )
            );
        }).then(() => self.skipWaiting())
    );
});

// Activate Event: Clean Old Caches & Claim Clients
self.addEventListener('activate', (event) => {
    event.waitUntil(
        caches.keys().then((cacheNames) => {
            return Promise.all(
                cacheNames.map((name) => {
                    if (name !== CACHE_NAME) {
                        return caches.delete(name);
                    }
                })
            );
        }).then(() => self.clients.claim())
    );
});

// Fetch Event
self.addEventListener('fetch', (event) => {
    const request = event.request;
    const url = new URL(request.url);

    // Skip non-GET requests (handled by client sync outbox)
    if (request.method !== 'GET') {
        return;
    }

    // Handle Sync Catalog API (Network first, fall back to cache)
    if (url.pathname.startsWith('/api/sync/catalog')) {
        event.respondWith(
            fetch(request)
                .then((response) => {
                    if (response.ok) {
                        const cloned = response.clone();
                        caches.open(CACHE_NAME).then((cache) => cache.put(request, cloned));
                    }
                    return response;
                })
                .catch(() => caches.match(request))
        );
        return;
    }

    // Handle HTML Navigation requests (Network-first with offline fallback)
    if (request.mode === 'navigate') {
        event.respondWith(
            fetch(request)
                .then((response) => {
                    if (response.ok) {
                        const cloned = response.clone();
                        caches.open(CACHE_NAME).then((cache) => cache.put(request, cloned));
                    }
                    return response;
                })
                .catch(async () => {
                    const cachedResponse = await caches.match(request);
                    if (cachedResponse) return cachedResponse;
                    return caches.match('/offline.html');
                })
        );
        return;
    }

    // Static Assets & Images: Cache-first with Stale-While-Revalidate
    event.respondWith(
        caches.match(request).then((cachedResponse) => {
            if (cachedResponse) {
                // Fetch in background to update cache
                fetch(request)
                    .then((networkResponse) => {
                        if (networkResponse.ok) {
                            caches.open(CACHE_NAME).then((cache) => cache.put(request, networkResponse));
                        }
                    })
                    .catch(() => { /* Ignore offline fetch errors */ });
                return cachedResponse;
            }

            return fetch(request)
                .then((networkResponse) => {
                    if (networkResponse.ok && (request.destination === 'image' || request.destination === 'style' || request.destination === 'script')) {
                        const cloned = networkResponse.clone();
                        caches.open(CACHE_NAME).then((cache) => cache.put(request, cloned));
                    }
                    return networkResponse;
                })
                .catch(() => {
                    if (request.destination === 'image') {
                        return caches.match('/images/default-book.svg');
                    }
                });
        })
    );
});

// Background Sync Listener (if supported by browser)
self.addEventListener('sync', (event) => {
    if (event.tag === 'sync-offline-bookstore') {
        event.waitUntil(
            self.clients.matchAll().then((clients) => {
                clients.forEach((client) => {
                    client.postMessage({ type: 'TRIGGER_OFFLINE_SYNC' });
                });
            })
        );
    }
});
