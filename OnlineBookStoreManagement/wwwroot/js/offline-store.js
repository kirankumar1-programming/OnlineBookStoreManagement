/**
 * offline-store.js
 * IndexedDB storage engine for caching books, categories, offline orders, reviews, and sync queue.
 */
const OfflineStore = (function () {
    const DB_NAME = 'BookStoreOfflineDB';
    const DB_VERSION = 1;
    let dbInstance = null;

    function openDB() {
        if (dbInstance) return Promise.resolve(dbInstance);

        return new Promise((resolve, reject) => {
            const request = indexedDB.open(DB_NAME, DB_VERSION);

            request.onupgradeneeded = function (event) {
                const db = event.target.result;

                // 1. Books store
                if (!db.objectStoreNames.contains('books')) {
                    const booksStore = db.createObjectStore('books', { keyPath: 'id' });
                    booksStore.createIndex('title', 'title', { unique: false });
                    booksStore.createIndex('author', 'author', { unique: false });
                    booksStore.createIndex('categoryId', 'categoryId', { unique: false });
                    booksStore.createIndex('price', 'price', { unique: false });
                }

                // 2. Categories store
                if (!db.objectStoreNames.contains('categories')) {
                    const catStore = db.createObjectStore('categories', { keyPath: 'id' });
                    catStore.createIndex('displayOrder', 'displayOrder', { unique: false });
                }

                // 3. Offline Orders store
                if (!db.objectStoreNames.contains('offline_orders')) {
                    const ordersStore = db.createObjectStore('offline_orders', { keyPath: 'clientSyncId' });
                    ordersStore.createIndex('status', 'status', { unique: false });
                    ordersStore.createIndex('orderDate', 'orderDate', { unique: false });
                }

                // 4. Offline Reviews store
                if (!db.objectStoreNames.contains('offline_reviews')) {
                    const reviewsStore = db.createObjectStore('offline_reviews', { keyPath: 'clientSyncId' });
                    reviewsStore.createIndex('bookId', 'bookId', { unique: false });
                    reviewsStore.createIndex('status', 'status', { unique: false });
                }

                // 5. Sync Queue store
                if (!db.objectStoreNames.contains('sync_queue')) {
                    const queueStore = db.createObjectStore('sync_queue', { keyPath: 'id', autoIncrement: true });
                    queueStore.createIndex('status', 'status', { unique: false });
                    queueStore.createIndex('type', 'type', { unique: false });
                }

                // 6. Meta Store
                if (!db.objectStoreNames.contains('meta')) {
                    db.createObjectStore('meta', { keyPath: 'key' });
                }
            };

            request.onsuccess = function (event) {
                dbInstance = event.target.result;
                resolve(dbInstance);
            };

            request.onerror = function (event) {
                console.error('[OfflineStore] Failed to open IndexedDB:', event.target.error);
                reject(event.target.error);
            };
        });
    }

    async function executeTransaction(storeName, mode, callback) {
        const db = await openDB();
        return new Promise((resolve, reject) => {
            const tx = db.transaction(storeName, mode);
            const store = tx.objectStore(storeName);
            const result = callback(store);

            tx.oncomplete = () => resolve(result.value !== undefined ? result.value : result);
            tx.onerror = () => reject(tx.error);
            tx.onabort = () => reject(tx.error);
        });
    }

    // --- Book & Catalog API ---
    async function saveBooks(books) {
        if (!Array.isArray(books) || books.length === 0) return;
        const db = await openDB();
        return new Promise((resolve, reject) => {
            const tx = db.transaction('books', 'readwrite');
            const store = tx.objectStore('books');
            books.forEach(b => store.put(b));
            tx.oncomplete = () => resolve(books.length);
            tx.onerror = () => reject(tx.error);
        });
    }

    async function getAllBooks() {
        const db = await openDB();
        return new Promise((resolve, reject) => {
            const tx = db.transaction('books', 'readonly');
            const store = tx.objectStore('books');
            const request = store.getAll();
            request.onsuccess = () => resolve(request.result || []);
            request.onerror = () => reject(request.error);
        });
    }

    async function getBookById(id) {
        const numId = parseInt(id, 10);
        const db = await openDB();
        return new Promise((resolve, reject) => {
            const tx = db.transaction('books', 'readonly');
            const store = tx.objectStore('books');
            const request = store.get(numId);
            request.onsuccess = () => resolve(request.result || null);
            request.onerror = () => reject(request.error);
        });
    }

    async function searchBooks(query, categoryId = null, minPrice = null, maxPrice = null, sortBy = 'default') {
        const allBooks = await getAllBooks();
        let filtered = allBooks;

        if (categoryId && categoryId > 0) {
            filtered = filtered.filter(b => b.categoryId === categoryId);
        }

        if (minPrice && minPrice > 0) {
            filtered = filtered.filter(b => b.price >= minPrice);
        }

        if (maxPrice && maxPrice > 0) {
            filtered = filtered.filter(b => b.price <= maxPrice);
        }

        if (query && query.trim().length > 0) {
            const terms = query.trim().toLowerCase().split(/\s+/);
            filtered = filtered.filter(b => {
                const title = (b.title || '').toLowerCase();
                const author = (b.author || '').toLowerCase();
                const isbn = (b.isbn || '').toLowerCase();
                const desc = (b.description || '').toLowerCase();
                return terms.every(t => title.includes(t) || author.includes(t) || isbn.includes(t) || desc.includes(t));
            });
        }

        // Sorting
        switch (sortBy) {
            case 'price_asc':
                filtered.sort((a, b) => a.price - b.price);
                break;
            case 'price_desc':
                filtered.sort((a, b) => b.price - a.price);
                break;
            case 'title_asc':
                filtered.sort((a, b) => (a.title || '').localeCompare(b.title || ''));
                break;
            case 'title_desc':
                filtered.sort((a, b) => (b.title || '').localeCompare(a.title || ''));
                break;
            case 'rating_desc':
                filtered.sort((a, b) => (b.averageRating || 0) - (a.averageRating || 0));
                break;
            default:
                filtered.sort((a, b) => b.id - a.id);
                break;
        }

        return filtered;
    }

    // --- Category API ---
    async function saveCategories(categories) {
        if (!Array.isArray(categories) || categories.length === 0) return;
        const db = await openDB();
        return new Promise((resolve, reject) => {
            const tx = db.transaction('categories', 'readwrite');
            const store = tx.objectStore('categories');
            categories.forEach(c => store.put(c));
            tx.oncomplete = () => resolve(categories.length);
            tx.onerror = () => reject(tx.error);
        });
    }

    async function getAllCategories() {
        const db = await openDB();
        return new Promise((resolve, reject) => {
            const tx = db.transaction('categories', 'readonly');
            const store = tx.objectStore('categories');
            const request = store.getAll();
            request.onsuccess = () => resolve(request.result || []);
            request.onerror = () => reject(request.error);
        });
    }

    // --- Offline Orders API ---
    async function saveOfflineOrder(order) {
        if (!order.clientSyncId) {
            order.clientSyncId = 'OFFLINE-' + Date.now() + '-' + Math.random().toString(36).substr(2, 9);
        }
        order.status = order.status || 'PendingSync';
        order.orderDate = order.orderDate || new Date().toISOString();

        const db = await openDB();
        return new Promise((resolve, reject) => {
            const tx = db.transaction(['offline_orders', 'sync_queue'], 'readwrite');
            const orderStore = tx.objectStore('offline_orders');
            const queueStore = tx.objectStore('sync_queue');

            orderStore.put(order);
            queueStore.add({
                type: 'Order',
                clientSyncId: order.clientSyncId,
                payload: order,
                status: 'Pending',
                createdAt: new Date().toISOString()
            });

            tx.oncomplete = () => resolve(order);
            tx.onerror = () => reject(tx.error);
        });
    }

    async function getOfflineOrders() {
        const db = await openDB();
        return new Promise((resolve, reject) => {
            const tx = db.transaction('offline_orders', 'readonly');
            const store = tx.objectStore('offline_orders');
            const request = store.getAll();
            request.onsuccess = () => resolve(request.result || []);
            request.onerror = () => reject(request.error);
        });
    }

    async function getOfflineOrder(clientSyncId) {
        const db = await openDB();
        return new Promise((resolve, reject) => {
            const tx = db.transaction('offline_orders', 'readonly');
            const store = tx.objectStore('offline_orders');
            const request = store.get(clientSyncId);
            request.onsuccess = () => resolve(request.result || null);
            request.onerror = () => reject(request.error);
        });
    }

    async function updateOfflineOrderStatus(clientSyncId, status, serverId = null, message = null) {
        const db = await openDB();
        return new Promise((resolve, reject) => {
            const tx = db.transaction('offline_orders', 'readwrite');
            const store = tx.objectStore('offline_orders');
            const getReq = store.get(clientSyncId);
            getReq.onsuccess = () => {
                const order = getReq.result;
                if (order) {
                    order.status = status;
                    if (serverId) order.serverId = serverId;
                    if (message) order.syncMessage = message;
                    order.syncedAt = new Date().toISOString();
                    store.put(order);
                }
            };
            tx.oncomplete = () => resolve();
            tx.onerror = () => reject(tx.error);
        });
    }

    // --- Offline Reviews API ---
    async function saveOfflineReview(review) {
        if (!review.clientSyncId) {
            review.clientSyncId = 'REV-' + Date.now() + '-' + Math.random().toString(36).substr(2, 9);
        }
        review.status = 'PendingSync';
        review.reviewDate = review.reviewDate || new Date().toISOString();

        const db = await openDB();
        return new Promise((resolve, reject) => {
            const tx = db.transaction(['offline_reviews', 'sync_queue'], 'readwrite');
            const revStore = tx.objectStore('offline_reviews');
            const queueStore = tx.objectStore('sync_queue');

            revStore.put(review);
            queueStore.add({
                type: 'Review',
                clientSyncId: review.clientSyncId,
                payload: review,
                status: 'Pending',
                createdAt: new Date().toISOString()
            });

            tx.oncomplete = () => resolve(review);
            tx.onerror = () => reject(tx.error);
        });
    }

    async function getOfflineReviews() {
        const db = await openDB();
        return new Promise((resolve, reject) => {
            const tx = db.transaction('offline_reviews', 'readonly');
            const store = tx.objectStore('offline_reviews');
            const request = store.getAll();
            request.onsuccess = () => resolve(request.result || []);
            request.onerror = () => reject(request.error);
        });
    }

    // --- Sync Queue API ---
    async function getPendingSyncQueue() {
        const db = await openDB();
        return new Promise((resolve, reject) => {
            const tx = db.transaction('sync_queue', 'readonly');
            const store = tx.objectStore('sync_queue');
            const request = store.getAll();
            request.onsuccess = () => {
                const all = request.result || [];
                resolve(all.filter(i => i.status === 'Pending'));
            };
            request.onerror = () => reject(request.error);
        });
    }

    async function clearSyncQueueItem(id) {
        const db = await openDB();
        return new Promise((resolve, reject) => {
            const tx = db.transaction('sync_queue', 'readwrite');
            const store = tx.objectStore('sync_queue');
            store.delete(id);
            tx.oncomplete = () => resolve();
            tx.onerror = () => reject(tx.error);
        });
    }

    async function clearAllPendingQueue() {
        const db = await openDB();
        return new Promise((resolve, reject) => {
            const tx = db.transaction('sync_queue', 'readwrite');
            const store = tx.objectStore('sync_queue');
            store.clear();
            tx.oncomplete = () => resolve();
            tx.onerror = () => reject(tx.error);
        });
    }

    async function getPendingCounts() {
        const pendingQueue = await getPendingSyncQueue();
        const orders = pendingQueue.filter(q => q.type === 'Order').length;
        const reviews = pendingQueue.filter(q => q.type === 'Review').length;
        return {
            total: pendingQueue.length,
            orders,
            reviews
        };
    }

    return {
        openDB,
        saveBooks,
        getAllBooks,
        getBookById,
        searchBooks,
        saveCategories,
        getAllCategories,
        saveOfflineOrder,
        getOfflineOrders,
        getOfflineOrder,
        updateOfflineOrderStatus,
        saveOfflineReview,
        getOfflineReviews,
        getPendingSyncQueue,
        clearSyncQueueItem,
        clearAllPendingQueue,
        getPendingCounts
    };
})();
