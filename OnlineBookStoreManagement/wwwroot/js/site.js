// Live Navbar Search with Auto-Complete Dropdown
document.addEventListener('DOMContentLoaded', function () {
    const searchInput = document.getElementById('navbarSearchInput');
    const searchClear = document.getElementById('navbarSearchClear');
    const searchIcon = document.getElementById('navbarSearchIcon');
    const searchSpinner = document.getElementById('navbarSearchSpinner');
    const searchResults = document.getElementById('navbarSearchResults');
    const searchContainer = document.getElementById('navbarSearchContainer');
    const searchForm = document.getElementById('navbarSearchForm');

    if (!searchInput || !searchResults) return;

    let debounceTimer = null;
    let abortController = null;
    let selectedIndex = -1;

    function escapeHtml(text) {
        if (!text) return '';
        return String(text)
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;")
            .replace(/'/g, "&#039;");
    }

    function highlightMatch(text, query) {
        if (!text || !query) return escapeHtml(text);
        const safeText = escapeHtml(text);
        const safeQuery = escapeHtml(query.trim());
        const regex = new RegExp(`(${safeQuery.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')})`, 'gi');
        return safeText.replace(regex, '<mark class="bg-primary text-white p-0 rounded-1">$1</mark>');
    }

    function showSpinner(show) {
        if (show) {
            if (searchIcon) searchIcon.classList.add('d-none');
            if (searchSpinner) searchSpinner.classList.remove('d-none');
        } else {
            if (searchSpinner) searchSpinner.classList.add('d-none');
            if (searchIcon) searchIcon.classList.remove('d-none');
        }
    }

    function toggleClearButton() {
        if (!searchClear) return;
        if (searchInput.value.trim().length > 0) {
            searchClear.classList.remove('d-none');
        } else {
            searchClear.classList.add('d-none');
        }
    }

    function hideDropdown() {
        searchResults.classList.add('d-none');
        searchResults.innerHTML = '';
        selectedIndex = -1;
    }

    function updateActiveItem(navigableItems) {
        navigableItems.forEach((item, idx) => {
            if (idx === selectedIndex) {
                item.classList.add('active');
                item.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
            } else {
                item.classList.remove('active');
            }
        });
    }

    function performSearch() {
        const query = searchInput.value.trim();

        if (query.length < 1) {
            hideDropdown();
            showSpinner(false);
            return;
        }

        showSpinner(true);

        if (abortController) {
            abortController.abort();
        }
        abortController = new AbortController();

        fetch(`/Home/LiveSearch?query=${encodeURIComponent(query)}`, {
            signal: abortController.signal
        })
            .then(response => {
                if (!response.ok) throw new Error('Network response error');
                return response.json();
            })
            .then(data => {
                showSpinner(false);
                renderDropdown(data, query);
            })
            .catch(error => {
                if (error.name === 'AbortError') return;
                showSpinner(false);
                console.error('Live search request failed:', error);
            });
    }

    function renderDropdown(books, query) {
        if (!Array.isArray(books) || books.length === 0) {
            searchResults.innerHTML = `
                <div class="navbar-search-empty">
                    <i class="bi bi-search-heart display-6 d-block mb-2 text-muted"></i>
                    <p class="mb-1 text-light fw-semibold">No books found matching "${escapeHtml(query)}"</p>
                    <small class="text-muted">Try searching by title, author, or ISBN</small>
                </div>
            `;
            searchResults.classList.remove('d-none');
            selectedIndex = -1;
            return;
        }

        let html = '';

        books.forEach((book, index) => {
            const priceFormatted = parseFloat(book.price).toFixed(2);
            html += `
                <a href="/Home/Details/${book.id}" class="navbar-search-item" data-nav-index="${index}">
                    <img src="${escapeHtml(book.coverImageUrl)}" alt="${escapeHtml(book.title)}" class="navbar-search-thumb" onerror="this.onerror=null; this.src='/images/default-book.svg';" />
                    <div class="flex-grow-1 min-w-0">
                        <div class="navbar-search-item-title">${highlightMatch(book.title, query)}</div>
                        <div class="navbar-search-item-meta d-flex align-items-center gap-2 flex-wrap">
                            <span>by ${escapeHtml(book.author)}</span>
                            ${book.category ? `<span class="badge bg-dark text-muted border border-secondary">${escapeHtml(book.category)}</span>` : ''}
                        </div>
                    </div>
                    <div class="text-end flex-shrink-0">
                        <div class="fw-bold text-accent small">₹${priceFormatted}</div>
                        <small class="${book.inStock ? 'text-success' : 'text-danger'} font-monospace" style="font-size: 0.7rem;">
                            ${book.inStock ? 'In Stock' : 'Out of Stock'}
                        </small>
                    </div>
                </a>
            `;
        });

        html += `
            <a href="/Home/Index?searchTerm=${encodeURIComponent(query)}" class="navbar-search-footer" data-nav-index="${books.length}">
                View all search results for "${escapeHtml(query)}" <i class="bi bi-arrow-right ms-1"></i>
            </a>
        `;

        searchResults.innerHTML = html;
        searchResults.classList.remove('d-none');
        selectedIndex = -1;
    }

    // Input Event Listener with Debounce (250ms)
    searchInput.addEventListener('input', function () {
        toggleClearButton();
        clearTimeout(debounceTimer);
        debounceTimer = setTimeout(performSearch, 250);
    });

    // Clear Button Click Listener
    if (searchClear) {
        searchClear.addEventListener('click', function () {
            searchInput.value = '';
            toggleClearButton();
            hideDropdown();
            searchInput.focus();
        });
    }

    // Focus Listener
    searchInput.addEventListener('focus', function () {
        if (searchInput.value.trim().length >= 1 && searchResults.children.length > 0) {
            searchResults.classList.remove('d-none');
        }
    });

    // Keyboard Navigation Listener
    searchInput.addEventListener('keydown', function (e) {
        const navigableItems = searchResults.querySelectorAll('.navbar-search-item, .navbar-search-footer');

        if (searchResults.classList.contains('d-none') || navigableItems.length === 0) {
            if (e.key === 'Enter' && searchInput.value.trim().length === 0) {
                e.preventDefault();
            }
            return;
        }

        if (e.key === 'ArrowDown') {
            e.preventDefault();
            selectedIndex++;
            if (selectedIndex >= navigableItems.length) {
                selectedIndex = 0;
            }
            updateActiveItem(navigableItems);
        } else if (e.key === 'ArrowUp') {
            e.preventDefault();
            selectedIndex--;
            if (selectedIndex < 0) {
                selectedIndex = navigableItems.length - 1;
            }
            updateActiveItem(navigableItems);
        } else if (e.key === 'Enter') {
            if (selectedIndex >= 0 && selectedIndex < navigableItems.length) {
                e.preventDefault();
                navigableItems[selectedIndex].click();
            }
        } else if (e.key === 'Escape') {
            hideDropdown();
            searchInput.blur();
        }
    });

    // Close Dropdown on Click Outside
    document.addEventListener('click', function (e) {
        if (searchContainer && !searchContainer.contains(e.target)) {
            hideDropdown();
        }
    });
});
