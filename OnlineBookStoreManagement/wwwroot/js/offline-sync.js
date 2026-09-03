/**
 * offline-sync.js
 * Comprehensive Offline Sync Engine: network listeners, Service Worker lifecycle,
 * IndexedDB background synchronization, outbox processor, and UI sync indicators.
 */

const OfflineSyncManager = (function () {
    let isSyncing = false;

    // --- 1. Service Worker & Catalog Pre-caching ---
    async function init() {
        registerServiceWorker();
        setupNetworkListeners();
        updateNetworkUI();

        // Initial sync of catalog if online
        if (navigator.onLine) {
            await refreshCatalogCache();
            await processPendingSync();
        }

        setupFormInterceptors();
        setupLiveSearchOfflineFallback();
    }

    function registerServiceWorker() {
        if ('serviceWorker' in navigator) {
            navigator.serviceWorker.register('/sw.js')
                .then((reg) => {
                    console.log('[PWA] Service Worker registered with scope:', reg.scope);
                })
                .catch((err) => {
                    console.warn('[PWA] Service Worker registration failed:', err);
                });

            // Listen for SW messages
            navigator.serviceWorker.addEventListener('message', (event) => {
                if (event.data && event.data.type === 'TRIGGER_OFFLINE_SYNC') {
                    processPendingSync();
                }
            });
        }
    }

    // --- 2. Network Status & UI Badges ---
    function setupNetworkListeners() {
        window.addEventListener('online', async () => {
            console.log('[Network] Back online!');
            updateNetworkUI();
            showToast('Connection Restored', 'You are back online. Synchronizing data with the main server...', 'info');
            await refreshCatalogCache();
            await processPendingSync();
        });

        window.addEventListener('offline', () => {
            console.warn('[Network] Disconnected from internet');
            updateNetworkUI();
            showToast('Offline Mode Active', 'No internet connection detected. Changes will be saved locally and synced automatically when back online.', 'warning');
        });
    }

    async function updateNetworkUI() {
        const isOnline = navigator.onLine;
        const badge = document.getElementById('offlineStatusBadge');
        const banner = document.getElementById('offlineModeBanner');
        const syncBtn = document.getElementById('navbarSyncBtn');
        const counts = await OfflineStore.getPendingCounts();

        if (badge) {
            if (isOnline) {
                if (counts.total > 0) {
                    badge.innerHTML = `<span class="badge bg-warning text-dark"><i class="bi bi-cloud-arrow-up me-1"></i>${counts.total} Pending Sync</span>`;
                } else {
                    badge.innerHTML = `<span class="badge bg-success-subtle text-success border border-success-subtle"><i class="bi bi-wifi me-1"></i>Online</span>`;
                }
            } else {
                badge.innerHTML = `<span class="badge bg-warning text-dark border border-warning"><i class="bi bi-wifi-off me-1"></i>Offline Mode (${counts.total} Queued)</span>`;
            }
        }

        if (banner) {
            if (!isOnline) {
                banner.classList.remove('d-none');
            } else {
                banner.classList.add('d-none');
            }
        }

        if (syncBtn) {
            const countBadge = syncBtn.querySelector('.sync-pending-count');
            if (countBadge) {
                countBadge.textContent = counts.total > 0 ? counts.total : '';
                countBadge.style.display = counts.total > 0 ? 'inline-block' : 'none';
            }
        }
    }

    function showToast(title, message, type = 'info') {
        let container = document.getElementById('offlineToastContainer');
        if (!container) {
            container = document.createElement('div');
            container.id = 'offlineToastContainer';
            container.className = 'toast-container position-fixed bottom-0 end-0 p-3';
            container.style.zIndex = '99999';
            document.body.appendChild(container);
        }

        const iconClass = type === 'warning' ? 'bi-exclamation-triangle-fill text-warning' :
                          type === 'success' ? 'bi-check-circle-fill text-success' :
                          type === 'danger' ? 'bi-x-octagon-fill text-danger' : 'bi-info-circle-fill text-primary';

        const toastEl = document.createElement('div');
        toastEl.className = 'toast align-items-center text-bg-dark border border-secondary shadow-lg';
        toastEl.setAttribute('role', 'alert');
        toastEl.setAttribute('aria-live', 'assertive');
        toastEl.setAttribute('aria-atomic', 'true');
        toastEl.innerHTML = `
            <div class="toast-header bg-dark text-light border-bottom border-secondary">
                <i class="bi ${iconClass} me-2"></i>
                <strong class="me-auto">${title}</strong>
                <small class="text-muted">Just now</small>
                <button type="button" class="btn-close btn-close-white" data-bs-dismiss="toast" aria-label="Close"></button>
            </div>
            <div class="toast-body text-light">
                ${message}
            </div>
        `;

        container.appendChild(toastEl);
        if (window.bootstrap && bootstrap.Toast) {
            const bsToast = new bootstrap.Toast(toastEl, { delay: 5000 });
            bsToast.show();
        } else {
            setTimeout(() => toastEl.remove(), 5000);
        }
    }

    // --- 3. Catalog Synchronization ---
    async function refreshCatalogCache() {
        if (!navigator.onLine) return;

        try {
            const response = await fetch('/api/sync/catalog', { cache: 'no-cache' });
            if (!response.ok) return;

            const data = await response.json();
            if (data && data.success) {
                if (data.books && data.books.length > 0) {
                    await OfflineStore.saveBooks(data.books);
                    console.log(`[OfflineStore] Cached ${data.books.length} books in IndexedDB.`);
                }
                if (data.categories && data.categories.length > 0) {
                    await OfflineStore.saveCategories(data.categories);
                }
            }
        } catch (err) {
            console.warn('[OfflineStore] Failed to refresh catalog cache:', err);
        }
    }

    // --- 4. Outbox Sync Processor ---
    async function processPendingSync() {
        if (isSyncing || !navigator.onLine) return;
        isSyncing = true;

        const syncBadge = document.getElementById('offlineStatusBadge');
        if (syncBadge) {
            syncBadge.innerHTML = `<span class="badge bg-primary text-white"><span class="spinner-border spinner-border-sm me-1"></span>Syncing...</span>`;
        }

        try {
            const pendingQueue = await OfflineStore.getPendingSyncQueue();
            const offlineCart = await OfflineStore.getOfflineCart();
            const offlineWishlist = await OfflineStore.getOfflineWishlist();

            const orders = (pendingQueue || []).filter(q => q.type === 'Order').map(q => q.payload);
            const reviews = (pendingQueue || []).filter(q => q.type === 'Review').map(q => q.payload);

            const hasDataToSync = orders.length > 0 || reviews.length > 0 || offlineCart.length > 0 || offlineWishlist.length > 0;

            if (hasDataToSync) {
                const payload = {
                    batchId: 'BATCH-' + Date.now(),
                    orders: orders,
                    reviews: reviews,
                    cartItems: offlineCart.map(c => ({ bookId: c.bookId, count: c.count })),
                    wishlistItems: offlineWishlist.map(w => ({ bookId: w.bookId }))
                };

                const response = await fetch('/api/sync/process', {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/json'
                    },
                    body: JSON.stringify(payload)
                });

                if (!response.ok) {
                    throw new Error(`Server returned HTTP ${response.status}`);
                }

                const result = await response.json();
                if (result && result.success) {
                    // Update local orders with server IDs
                    if (result.results && Array.isArray(result.results)) {
                        for (const item of result.results) {
                            if (item.type === 'Order') {
                                await OfflineStore.updateOfflineOrderStatus(item.clientSyncId, item.status === 'Success' || item.status === 'Skipped' ? 'Synced' : item.status, item.serverId, item.message);
                            }
                        }
                    }

                    // Clear synced queue items & offline cart
                    await OfflineStore.clearAllPendingQueue();
                    await OfflineStore.clearOfflineCart();

                    showToast(
                        'Sync Completed',
                        `Successfully synchronized offline data with the central database!`,
                        'success'
                    );

                    // Re-fresh catalog to get updated stock
                    await refreshCatalogCache();

                    // Dispatch global event
                    window.dispatchEvent(new CustomEvent('bookstore:synced', { detail: result }));
                } else {
                    showToast('Sync Warning', result.summaryMessage || 'Some items could not be synchronized.', 'warning');
                }
            } else {
                // Background check to align databases
                await fetch('/api/sync/trigger-server-sync', { method: 'POST' }).catch(() => {});
            }
        } catch (err) {
            console.error('[OfflineSync] Batch synchronization error:', err);
            showToast('Sync Failed', 'Could not reach server database. Will retry automatically when connection is stable.', 'danger');
        } finally {
            isSyncing = false;
            await updateNetworkUI();
        }
    }

    // --- 5. Intercept Forms & Links for Seamless Offline Operation ---
    function setupFormInterceptors() {
        document.addEventListener('submit', async function (e) {
            const form = e.target;
            if (!form) return;

            const action = (form.getAttribute('action') || form.action || '').toLowerCase();

            // A. Intercept Add to Cart Form if offline
            if (action.includes('/cart/addtocart')) {
                if (!navigator.onLine) {
                    e.preventDefault();
                    const formData = new FormData(form);
                    const bookId = parseInt(formData.get('bookId') || '0', 10);
                    const quantity = parseInt(formData.get('quantity') || '1', 10);

                    if (bookId > 0) {
                        await OfflineStore.addToOfflineCart(bookId, quantity);
                        await updateNetworkUI();
                        showToast('Added to Cart (Offline)', 'Book has been added to your local offline cart. It will sync automatically when back online.', 'success');
                    }
                    return;
                }
            }

            // B. Intercept Cart Quantity & Mutation Forms if offline
            if (action.includes('/cart/updatequantity') || action.includes('/cart/plus') || action.includes('/cart/minus') || action.includes('/cart/remove')) {
                if (!navigator.onLine) {
                    e.preventDefault();
                    const formData = new FormData(form);
                    const bookId = parseInt(formData.get('bookId') || formData.get('cartId') || '0', 10);
                    const quantity = parseInt(formData.get('quantity') || '1', 10);

                    if (action.includes('/cart/remove')) {
                        await OfflineStore.removeOfflineCartItem(bookId);
                        showToast('Offline Cart Updated', 'Item removed from offline cart.', 'info');
                    } else {
                        await OfflineStore.updateOfflineCartQuantity(bookId, quantity);
                        showToast('Offline Cart Updated', 'Cart quantity updated locally.', 'info');
                    }
                    await updateNetworkUI();
                    return;
                }
            }

            // C. Intercept Wishlist Toggle if offline
            if (action.includes('/wishlist/toggle') || action.includes('/wishlist/remove')) {
                if (!navigator.onLine) {
                    e.preventDefault();
                    const formData = new FormData(form);
                    const bookId = parseInt(formData.get('bookId') || '0', 10);
                    if (bookId > 0) {
                        const added = await OfflineStore.toggleOfflineWishlist(bookId);
                        showToast('Offline Wishlist', added ? 'Book added to offline wishlist.' : 'Book removed from offline wishlist.', 'info');
                        const btn = form.querySelector('button');
                        if (btn) {
                            btn.classList.toggle('active');
                            btn.classList.toggle('text-danger');
                        }
                    }
                    return;
                }
            }

            // D. Intercept Checkout Form if offline
            if (form.id === 'checkoutForm' || action.includes('/cart/checkout')) {
                if (!navigator.onLine) {
                    e.preventDefault();
                    await handleOfflineCheckout(form);
                    return;
                }
            }

            // E. Intercept Review Form if offline
            if (form.id === 'addReviewForm' || action.includes('/home/addreview')) {
                if (!navigator.onLine) {
                    e.preventDefault();
                    await handleOfflineReview(form);
                    return;
                }
            }
        });

        // Intercept navigation links when offline to route to offline storefront
        document.addEventListener('click', function (e) {
            if (!navigator.onLine) {
                const link = e.target.closest('a');
                if (link && link.href && link.origin === window.location.origin) {
                    const pathname = new URL(link.href).pathname.toLowerCase();
                    if (pathname.startsWith('/cart') || pathname.startsWith('/wishlist') || pathname === '/' || pathname === '/home' || pathname === '/home/index') {
                        e.preventDefault();
                        window.location.href = '/offline.html';
                    }
                }
            }
        });
    }

    async function handleOfflineCheckout(form) {
        const formData = new FormData(form);

        // Collect form fields
        const name = formData.get('OrderHeader.Name') || '';
        const phoneNumber = formData.get('OrderHeader.PhoneNumber') || '';
        const streetAddress = formData.get('OrderHeader.StreetAddress') || '';
        const city = formData.get('OrderHeader.City') || '';
        const postalCode = formData.get('OrderHeader.PostalCode') || '';
        const paymentType = formData.get('paymentType') || 'upi';
        const couponCode = formData.get('CouponCode') || '';

        // Validation
        if (!name || !phoneNumber || !streetAddress || !city || !postalCode) {
            alert('Please fill in all required shipping and contact details.');
            return;
        }

        // Collect Cart items from DOM
        const items = [];
        const itemRows = document.querySelectorAll('.checkout-cart-item');
        let total = 0;

        itemRows.forEach(row => {
            const bookId = parseInt(row.getAttribute('data-book-id'), 10);
            const title = row.getAttribute('data-book-title') || 'Book';
            const count = parseInt(row.getAttribute('data-book-count') || '1', 10);
            const price = parseFloat(row.getAttribute('data-book-price') || '0');

            if (bookId && count > 0) {
                items.push({ bookId, title, count, price });
                total += (price * count);
            }
        });

        // If items couldn't be parsed from DOM, read from input fields or default
        if (items.length === 0) {
            const hiddenBookIds = form.querySelectorAll('input[name="bookId"]');
            hiddenBookIds.forEach(input => {
                items.push({
                    bookId: parseInt(input.value, 10),
                    title: 'Book Item',
                    count: 1,
                    price: 0
                });
            });
        }

        const clientSyncId = 'OFFLINE-ORD-' + Date.now();
        const orderData = {
            clientSyncId: clientSyncId,
            name: name,
            phoneNumber: phoneNumber,
            streetAddress: streetAddress,
            city: city,
            postalCode: postalCode,
            paymentType: paymentType,
            couponCode: couponCode,
            discountAmount: 0,
            orderTotal: total,
            orderDate: new Date().toISOString(),
            items: items,
            status: 'PendingSync'
        };

        await OfflineStore.saveOfflineOrder(orderData);
        await updateNetworkUI();

        // Render offline confirmation view
        renderOfflineOrderConfirmation(orderData);
    }

    function renderOfflineOrderConfirmation(order) {
        const main = document.querySelector('main') || document.body;
        main.innerHTML = `
            <div class="container py-5">
                <div class="card bg-dark border-secondary shadow-lg rounded-4 overflow-hidden max-w-3xl mx-auto p-4 p-md-5 text-light">
                    <div class="text-center mb-4">
                        <div class="d-inline-flex p-3 rounded-circle bg-warning bg-opacity-10 text-warning mb-3">
                            <i class="bi bi-cloud-arrow-up-fill display-4"></i>
                        </div>
                        <h2 class="fw-bold mb-2">Order Saved Offline!</h2>
                        <span class="badge bg-warning text-dark px-3 py-2 rounded-pill fs-6 mb-3">
                            <i class="bi bi-clock-history me-1"></i> Pending Server Database Sync
                        </span>
                        <p class="text-muted">
                            Your order has been recorded securely in local offline storage on this device.
                            As soon as internet connectivity is restored, it will automatically sync to the server's database and deduct inventory.
                        </p>
                    </div>

                    <div class="bg-dark-subtle p-4 rounded-3 border border-secondary mb-4">
                        <div class="row g-3">
                            <div class="col-sm-6">
                                <small class="text-muted d-block">Offline Reference ID</small>
                                <span class="fw-mono fw-bold text-primary">${order.clientSyncId}</span>
                            </div>
                            <div class="col-sm-6">
                                <small class="text-muted d-block">Recipient Name</small>
                                <span class="fw-semibold">${order.name}</span>
                            </div>
                            <div class="col-sm-6">
                                <small class="text-muted d-block">Delivery Address</small>
                                <span>${order.streetAddress}, ${order.city} - ${order.postalCode}</span>
                            </div>
                            <div class="col-sm-6">
                                <small class="text-muted d-block">Payment Method</small>
                                <span class="text-accent fw-semibold">${order.paymentType.toUpperCase()}</span>
                            </div>
                        </div>
                    </div>

                    <div class="d-flex flex-wrap gap-3 justify-content-center">
                        <a href="/" class="btn btn-primary-gradient px-4 py-2 rounded-pill">
                            <i class="bi bi-house me-1"></i> Return to Store Catalog
                        </a>
                        <button type="button" class="btn btn-outline-secondary px-4 py-2 rounded-pill" onclick="OfflineSyncManager.processPendingSync()">
                            <i class="bi bi-arrow-repeat me-1"></i> Check Connection &amp; Sync Now
                        </button>
                    </div>
                </div>
            </div>
        `;
    }

    async function handleOfflineReview(form) {
        const formData = new FormData(form);
        const bookId = parseInt(formData.get('bookId'), 10);
        const rating = parseInt(formData.get('rating') || '5', 10);
        const comment = formData.get('comment') || '';

        if (!bookId || !comment.trim()) {
            alert('Please select a star rating and enter a review comment.');
            return;
        }

        const reviewData = {
            clientSyncId: 'OFFLINE-REV-' + Date.now(),
            bookId: bookId,
            rating: rating,
            comment: comment.trim(),
            reviewDate: new Date().toISOString()
        };

        await OfflineStore.saveOfflineReview(reviewData);
        await updateNetworkUI();

        showToast(
            'Review Saved Offline',
            'Your rating and review were saved to local storage and will sync automatically when back online.',
            'success'
        );

        // Reset form
        form.reset();
    }

    // --- 6. Live Search Offline Fallback ---
    function setupLiveSearchOfflineFallback() {
        const searchInput = document.getElementById('navbarSearchInput');
        const searchResults = document.getElementById('navbarSearchResults');

        if (!searchInput || !searchResults) return;

        // Hook window onerror / fallback for live search
        window.addEventListener('offlineSearchRequest', async (e) => {
            const query = e.detail?.query || '';
            const results = await OfflineStore.searchBooks(query);
            if (window.renderNavbarSearchResults) {
                window.renderNavbarSearchResults(results, query);
            }
        });
    }

    // --- 7. Server Database Sync (Azure SQL <-> Local SQLite) ---
    async function checkServerDatabaseStatus() {
        const badge = document.getElementById('serverDbStatusBadge');
        const msg = document.getElementById('serverDbStatusMsg');
        if (!badge) return;

        try {
            const res = await fetch('/api/sync/server-status', { cache: 'no-cache' });
            if (res.ok) {
                const data = await res.json();
                if (data.isServerOnline) {
                    badge.className = 'badge bg-success';
                    badge.innerHTML = '<i class="bi bi-cloud-check-fill me-1"></i> Connected';
                    if (msg && data.lastSyncMessage) msg.textContent = data.lastSyncMessage;
                } else {
                    badge.className = 'badge bg-warning text-dark';
                    badge.innerHTML = '<i class="bi bi-cloud-slash me-1"></i> Offline Mode';
                    if (msg) msg.textContent = 'Server database is currently offline. Operating 100% locally with zero downtime.';
                }
            }
        } catch (e) {
            badge.className = 'badge bg-warning text-dark';
            badge.innerHTML = '<i class="bi bi-cloud-slash me-1"></i> Offline Mode';
            if (msg) msg.textContent = 'Operating in offline local mode.';
        }
    }

    async function triggerServerDatabaseSync() {
        const badge = document.getElementById('serverDbStatusBadge');
        if (badge) {
            badge.className = 'badge bg-primary';
            badge.innerHTML = '<span class="spinner-border spinner-border-sm me-1"></span> Syncing...';
        }

        try {
            const res = await fetch('/api/sync/trigger-server-sync', { method: 'POST' });
            if (res.ok) {
                const data = await res.json();
                if (data.isConnected) {
                    showToast('Server DB Synced', data.message || 'Successfully synchronized local and server databases.', 'success');
                } else {
                    showToast('Server DB Offline', data.message || 'Server database unreachable. Local mode remains active.', 'warning');
                }
                await checkServerDatabaseStatus();
            }
        } catch (e) {
            showToast('Sync Offline', 'Server database is currently offline. Your changes are safe locally.', 'warning');
            await checkServerDatabaseStatus();
        }
    }

    // Public API
    return {
        init,
        refreshCatalogCache,
        processPendingSync,
        updateNetworkUI,
        showToast,
        checkServerDatabaseStatus,
        triggerServerDatabaseSync
    };
})();

// Hook modal show to check server DB status
document.addEventListener('DOMContentLoaded', () => {
    const syncModal = document.getElementById('offlineSyncModal');
    if (syncModal) {
        syncModal.addEventListener('show.bs.modal', () => {
            OfflineSyncManager.checkServerDatabaseStatus();
        });
    }
});

// Auto-initialize when DOM ready
document.addEventListener('DOMContentLoaded', () => {
    OfflineSyncManager.init();
});
