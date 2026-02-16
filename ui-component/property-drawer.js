/**
 * PropertyDrawer - Standalone vanilla JS module for Cook County property data
 * 
 * Supports two rendering modes:
 *   1. DRAWER mode (default): Slide-in panel for enterprise platform integration
 *   2. DASHBOARD mode: Single-screen card grid for standalone viewing
 * 
 * Integration (Drawer mode):
 *   PropertyDrawer.configure({ apiBaseUrl: 'https://your-api-host.com' });
 *   PropertyDrawer.open('01-01-120-006-0000');
 * 
 * Integration (Dashboard mode):
 *   PropertyDrawer.configure({ apiBaseUrl: '', mode: 'dashboard', dashboardContainerId: 'my-dashboard' });
 *   PropertyDrawer.open('01-01-120-006-0000');
 */
var PropertyDrawer = (function () {
  'use strict';

  var config = {
    apiBaseUrl: '',
    containerId: 'property-drawer-container',
    dashboardContainerId: 'pd-dashboard',
    mode: 'drawer',
    pinList: [],
    onOpen: null,
    onClose: null,
    onNavigate: null,
  };

  var state = {
    currentPin: null,
    abortController: null,
    cachedTimestamps: [],
    closeTimer: null,
  };

  function configure(opts) {
    if (opts.apiBaseUrl != null) config.apiBaseUrl = String(opts.apiBaseUrl).replace(/\/$/, '');
    if (opts.containerId) config.containerId = opts.containerId;
    if (opts.dashboardContainerId) config.dashboardContainerId = opts.dashboardContainerId;
    if (opts.mode) config.mode = opts.mode;
    if (Array.isArray(opts.pinList)) config.pinList = opts.pinList;
    if (opts.onOpen) config.onOpen = opts.onOpen;
    if (opts.onClose) config.onClose = opts.onClose;
    if (opts.onNavigate) config.onNavigate = opts.onNavigate;
  }

  function setPinList(pins) {
    if (Array.isArray(pins)) config.pinList = pins;
    updateNavDisplay();
  }

  function navigatePrev() {
    if (config.pinList.length === 0) return;
    var idx = config.pinList.indexOf(state.currentPin);
    if (idx === -1) idx = 0;
    var newIdx = idx <= 0 ? config.pinList.length - 1 : idx - 1;
    var pin = config.pinList[newIdx];
    open(pin);
    if (config.onNavigate) config.onNavigate(pin, newIdx, config.pinList.length);
  }

  function navigateNext() {
    if (config.pinList.length === 0) return;
    var idx = config.pinList.indexOf(state.currentPin);
    if (idx === -1) idx = -1;
    var newIdx = idx >= config.pinList.length - 1 ? 0 : idx + 1;
    var pin = config.pinList[newIdx];
    open(pin);
    if (config.onNavigate) config.onNavigate(pin, newIdx, config.pinList.length);
  }

  function updateNavDisplay() {
    var navEls = document.querySelectorAll('[data-pd-nav]');
    if (navEls.length === 0) return;
    var idx = config.pinList.indexOf(state.currentPin);
    var text = (idx === -1 ? '?' : (idx + 1)) + ' of ' + config.pinList.length;
    for (var i = 0; i < navEls.length; i++) {
      if (config.pinList.length === 0) {
        navEls[i].style.display = 'none';
      } else {
        navEls[i].style.display = '';
        var counterEl = navEls[i].querySelector('[data-pd-nav-counter]');
        if (counterEl) counterEl.textContent = text;
      }
    }
  }

  function loadBids(pin) {
    fetch(config.apiBaseUrl + '/api/pins/' + encodeURIComponent(pin) + '/bids')
      .then(function (res) { return res.json(); })
      .then(function (json) {
        if (json.success && state.currentPin === pin) {
          var bidInputs = document.querySelectorAll('[data-pd-bid]');
          var overbidInputs = document.querySelectorAll('[data-pd-overbid]');
          for (var i = 0; i < bidInputs.length; i++) {
            bidInputs[i].value = json.data.bid || '';
          }
          for (var j = 0; j < overbidInputs.length; j++) {
            overbidInputs[j].value = json.data.overbid || '';
          }
        }
      })
      .catch(function (err) {
        console.warn('PropertyDrawer: Failed to load bids for', pin, err);
      });
  }

  function saveBids(pin) {
    var bidEl = document.querySelector('[data-pd-bid]');
    var overbidEl = document.querySelector('[data-pd-overbid]');
    var bid = bidEl ? bidEl.value.trim() : '';
    var overbid = overbidEl ? overbidEl.value.trim() : '';
    fetch(config.apiBaseUrl + '/api/pins/' + encodeURIComponent(pin) + '/bids', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ bid: bid, overbid: overbid }),
    })
      .then(function (res) { return res.json(); })
      .then(function (json) {
        if (!json.success) console.warn('PropertyDrawer: Failed to save bids:', json.error);
      })
      .catch(function (err) {
        console.warn('PropertyDrawer: Failed to save bids for', pin, err);
      });
  }

  function syncBidInputs(sourceEl) {
    var attr = sourceEl.hasAttribute('data-pd-bid') ? 'data-pd-bid' : 'data-pd-overbid';
    var all = document.querySelectorAll('[' + attr + ']');
    for (var i = 0; i < all.length; i++) {
      if (all[i] !== sourceEl) all[i].value = sourceEl.value;
    }
  }

  function bindBidEvents(container) {
    var bidInputs = container.querySelectorAll('[data-pd-bid], [data-pd-overbid]');
    for (var i = 0; i < bidInputs.length; i++) {
      bidInputs[i].addEventListener('blur', function () {
        syncBidInputs(this);
        if (state.currentPin) saveBids(state.currentPin);
      });
      bidInputs[i].addEventListener('keydown', function (e) {
        if (e.key === 'Enter') {
          this.blur();
        }
      });
    }
  }

  function getContainer() {
    var el = document.getElementById(config.containerId);
    if (!el) {
      el = document.createElement('div');
      el.id = config.containerId;
      document.body.appendChild(el);
    }
    return el;
  }

  function open(pin) {
    if (!pin) return;
    pin = pin.trim();
    if (!validatePin(pin)) {
      console.warn('PropertyDrawer: Invalid PIN format. Expected XX-XX-XXX-XXX-XXXX, got:', pin);
      return;
    }
    state.currentPin = pin;
    state.cachedTimestamps = [];

    if (state.abortController) {
      state.abortController.abort();
    }
    state.abortController = new AbortController();

    if (config.mode === 'dashboard') {
      openDashboard(pin);
    } else {
      openDrawer(pin);
    }

    updateNavDisplay();
    fetchAllSources(pin, state.abortController.signal);
    loadBids(pin);
    if (config.onOpen) config.onOpen(pin);
  }

  function openDrawer(pin) {
    if (state.closeTimer) {
      clearTimeout(state.closeTimer);
      state.closeTimer = null;
    }
    var container = getContainer();
    var existingDrawer = container.querySelector('.drawer-main.open');
    if (existingDrawer) {
      var body = existingDrawer.querySelector('.drawer-body');
      if (body) body.innerHTML = buildDrawerGridHTML(pin);
      var pinEl = existingDrawer.querySelector('[data-testid="text-drawer-pin"]');
      if (pinEl) pinEl.textContent = 'PIN: ' + pin;
      var searchInput = existingDrawer.querySelector('[data-pd-pin-input]');
      if (searchInput) searchInput.value = pin;
      var bidInput = existingDrawer.querySelector('[data-pd-bid]');
      var overbidInput = existingDrawer.querySelector('[data-pd-overbid]');
      if (bidInput) bidInput.value = '';
      if (overbidInput) overbidInput.value = '';
      return;
    }
    container.innerHTML = buildDrawerHTML(pin);
    document.body.style.overflow = 'hidden';
    var drawerEl = container.querySelector('.drawer-main');
    var backdropEl = container.querySelector('[data-pd-backdrop]');
    if (drawerEl) {
      setTimeout(function () {
        drawerEl.classList.add('open');
        if (backdropEl) backdropEl.classList.add('open');
      }, 10);
    }
    if (backdropEl) {
      backdropEl.addEventListener('click', close);
    }
    bindDrawerEvents(container);
  }

  function openDashboard(pin) {
    var container = document.getElementById(config.dashboardContainerId);
    if (!container) return;
    container.classList.add('property-drawer');
    container.innerHTML = buildDashboardHTML(pin);
    bindNavEvents(container);
    bindBidEvents(container);
  }

  function bindNavEvents(container) {
    var prevBtn = container.querySelector('[data-pd-nav-prev]');
    var nextBtn = container.querySelector('[data-pd-nav-next]');
    if (prevBtn) prevBtn.addEventListener('click', navigatePrev);
    if (nextBtn) nextBtn.addEventListener('click', navigateNext);
  }

  function close() {
    if (config.mode === 'dashboard') {
      var container = document.getElementById(config.dashboardContainerId);
      if (container) container.innerHTML = '';
    } else {
      var drawerContainer = getContainer();
      var drawerEl = drawerContainer.querySelector('.drawer-main');
      var backdropEl = drawerContainer.querySelector('[data-pd-backdrop]');
      document.body.style.overflow = '';
      if (drawerEl) {
        drawerEl.classList.remove('open');
        if (backdropEl) backdropEl.classList.remove('open');
        state.closeTimer = setTimeout(function () {
          drawerContainer.innerHTML = '';
          state.closeTimer = null;
        }, 350);
      } else {
        drawerContainer.innerHTML = '';
      }
    }
    if (state.abortController) {
      state.abortController.abort();
      state.abortController = null;
    }
    state.currentPin = null;
    if (config.onClose) config.onClose();
  }

  function search(pin) {
    open(pin);
  }

  function bindDrawerEvents(container) {
    var closeBtn = container.querySelector('[data-pd-close]');
    if (closeBtn) closeBtn.addEventListener('click', close);

    bindNavEvents(container);
    bindBidEvents(container);

    var searchBtn = container.querySelector('[data-pd-search]');
    var searchInput = container.querySelector('[data-pd-pin-input]');
    if (searchBtn && searchInput) {
      searchBtn.addEventListener('click', function () {
        var val = searchInput.value.trim();
        if (val && validatePin(val)) search(val);
      });
      searchInput.addEventListener('keydown', function (e) {
        if (e.key === 'Enter') {
          var val = searchInput.value.trim();
          if (val && validatePin(val)) search(val);
        }
      });
    }
  }

  function validatePin(pin) {
    return /^\d{2}-\d{2}-\d{3}-\d{3}-\d{4}$/.test(pin);
  }

  // ── Dashboard HTML Template ──

  function buildDashboardHTML(pin) {
    return (
      '<div class="pd-dashboard-header" data-testid="dashboard-header">' +
        '<div class="pd-dashboard-header-left">' +
          '<span class="pd-dashboard-pin" data-testid="text-dashboard-pin">PIN: ' + escapeHtml(pin) + '</span>' +
          '<span class="pd-dashboard-last-updated" data-pd-last-updated data-testid="text-last-updated" style="display:none;"></span>' +
        '</div>' +
        '<div class="pd-bids" data-testid="bid-fields">' +
          '<label class="pd-bid-label">Bid <input type="text" inputmode="decimal" class="pd-bid-input" data-pd-bid data-testid="input-bid" placeholder="0.00"></label>' +
          '<label class="pd-bid-label">Overbid <input type="text" inputmode="decimal" class="pd-bid-input" data-pd-overbid data-testid="input-overbid" placeholder="0.00"></label>' +
        '</div>' +
        '<div class="pd-nav" data-pd-nav data-testid="nav-arrows" style="' + (config.pinList.length === 0 ? 'display:none;' : '') + '">' +
          '<button type="button" class="pd-nav-btn" data-pd-nav-prev data-testid="button-nav-prev" title="Previous PIN">' +
            '<svg width="14" height="14" viewBox="0 0 16 16" fill="currentColor"><path d="M11.354 1.646a.5.5 0 010 .708L5.707 8l5.647 5.646a.5.5 0 01-.708.708l-6-6a.5.5 0 010-.708l6-6a.5.5 0 01.708 0z"/></svg>' +
          '</button>' +
          '<span class="pd-nav-counter" data-pd-nav-counter data-testid="text-nav-counter"></span>' +
          '<button type="button" class="pd-nav-btn" data-pd-nav-next data-testid="button-nav-next" title="Next PIN">' +
            '<svg width="14" height="14" viewBox="0 0 16 16" fill="currentColor"><path d="M4.646 1.646a.5.5 0 01.708 0l6 6a.5.5 0 010 .708l-6 6a.5.5 0 01-.708-.708L10.293 8 4.646 2.354a.5.5 0 010-.708z"/></svg>' +
          '</button>' +
        '</div>' +
      '</div>' +
      '<div class="pd-dashboard-grid" data-testid="property-dashboard">' +
        '<div class="pd-row pd-row-top">' +
          buildCardShell('tax-portal', 'Property Info', 'cookcountypropertyinfo.com', 'https://www.cookcountypropertyinfo.com/') +
          buildCardShell('characteristics', 'Characteristics', 'cookcountypropertyinfo.com', 'https://www.cookcountypropertyinfo.com/') +
          buildCardShell('tax-bills', 'Tax Bills & Payments', '', '') +
          buildCardShell('gis-map', 'GIS / Parcel Map', 'maps.cookcountyil.gov', 'https://maps.cookcountyil.gov/cookviewer/') +
          buildCardShell('google-maps', 'Google Maps', '', '') +
        '</div>' +
        '<div class="pd-row pd-row-tax-sale">' +
          buildCardShell('tax-sale', 'Tax Sale -- Tax Portal', '', '') +
        '</div>' +
        '<div class="pd-row pd-row-bottom">' +
          buildCardShell('sold-taxes', 'Sold Taxes -- Clerk', 'cookcountyclerkil.gov', 'https://taxdelinquent.cookcountyclerkil.gov/') +
          buildCardShell('delinquent-taxes', 'Delinquent Taxes -- Clerk', '', '') +
          buildCardShell('recorder', 'Recorded Documents', 'cookcountyclerkil.gov/recorder', 'https://crs.cookcountyclerkil.gov/') +
        '</div>' +
      '</div>'
    );
  }

  function buildCardShell(id, title, sourceName, sourceUrl) {
    var sourceLink = '';
    if (sourceName && sourceUrl) {
      sourceLink = ' <a href="' + sourceUrl + '" target="_blank" rel="noopener noreferrer" class="pd-source-link" data-testid="link-source-' + id + '" title="Open source website">&#x2197;</a>';
    }
    return (
      '<div class="pd-card loading" id="pd-' + id + '" data-testid="section-' + id + '">' +
        '<div class="pd-card-header">' +
          '<h6>' + escapeHtml(title) + sourceLink + '</h6>' +
        '</div>' +
        '<div class="pd-card-body pd-section-body">' +
          '<div class="pd-skeleton w-100" style="margin-bottom:6px;"></div>' +
          '<div class="pd-skeleton w-75" style="margin-bottom:6px;"></div>' +
          '<div class="pd-skeleton w-50"></div>' +
        '</div>' +
      '</div>'
    );
  }

  // ── Drawer HTML Template ──

  function buildDrawerGridHTML(pin) {
    return (
      '<div class="pd-dashboard-grid" data-testid="property-dashboard">' +
        '<div class="pd-row pd-row-top">' +
          buildCardShell('tax-portal', 'Property Info', 'cookcountypropertyinfo.com', 'https://www.cookcountypropertyinfo.com/') +
          buildCardShell('characteristics', 'Characteristics', 'cookcountypropertyinfo.com', 'https://www.cookcountypropertyinfo.com/') +
          buildCardShell('tax-bills', 'Tax Bills & Payments', '', '') +
          buildCardShell('gis-map', 'GIS / Parcel Map', 'maps.cookcountyil.gov', 'https://maps.cookcountyil.gov/cookviewer/') +
          buildCardShell('google-maps', 'Google Maps', '', '') +
        '</div>' +
        '<div class="pd-row pd-row-tax-sale">' +
          buildCardShell('tax-sale', 'Tax Sale -- Tax Portal', '', '') +
        '</div>' +
        '<div class="pd-row pd-row-bottom">' +
          buildCardShell('sold-taxes', 'Sold Taxes -- Clerk', 'cookcountyclerkil.gov', 'https://taxdelinquent.cookcountyclerkil.gov/') +
          buildCardShell('delinquent-taxes', 'Delinquent Taxes -- Clerk', '', '') +
          buildCardShell('recorder', 'Recorded Documents', 'cookcountyclerkil.gov/recorder', 'https://crs.cookcountyclerkil.gov/') +
        '</div>' +
      '</div>'
    );
  }

  function buildDrawerHTML(pin) {
    return (
      '<div class="pd-drawer-backdrop" data-pd-backdrop data-testid="drawer-backdrop"></div>' +
      '<div class="drawer-main property-drawer" data-testid="property-drawer">' +
        '<div class="drawer-header">' +
          '<div class="drawer-header-top">' +
            '<h5 class="drawer-title">Property Details</h5>' +
            '<div class="drawer-header-controls">' +
              '<div class="pd-search-row">' +
                '<input type="text" class="form-control" data-pd-pin-input data-testid="input-drawer-pin" ' +
                  'placeholder="XX-XX-XXX-XXX-XXXX" value="' + escapeHtml(pin) + '">' +
                '<button type="button" class="btn btn-light" data-pd-search data-testid="button-drawer-search">Search</button>' +
              '</div>' +
              '<button type="button" class="btn btn-link drawer-close-btn" data-pd-close data-testid="button-close-drawer">&times;</button>' +
            '</div>' +
          '</div>' +
          '<div class="drawer-header-bottom">' +
            '<span class="pd-dashboard-pin" data-testid="text-drawer-pin">PIN: ' + escapeHtml(pin) + '</span>' +
            '<span class="pd-dashboard-last-updated" data-pd-last-updated data-testid="text-last-updated" style="display:none;"></span>' +
            '<div class="drawer-header-right">' +
              '<div class="pd-bids" data-testid="bid-fields">' +
                '<label class="pd-bid-label">Bid <input type="text" inputmode="decimal" class="pd-bid-input" data-pd-bid data-testid="input-bid" placeholder="0.00"></label>' +
                '<label class="pd-bid-label">Overbid <input type="text" inputmode="decimal" class="pd-bid-input" data-pd-overbid data-testid="input-overbid" placeholder="0.00"></label>' +
              '</div>' +
              '<div class="pd-nav" data-pd-nav data-testid="nav-arrows" style="' + (config.pinList.length === 0 ? 'display:none;' : '') + '">' +
                '<button type="button" class="pd-nav-btn" data-pd-nav-prev data-testid="button-nav-prev" title="Previous PIN">' +
                  '<svg width="14" height="14" viewBox="0 0 16 16" fill="currentColor"><path d="M11.354 1.646a.5.5 0 010 .708L5.707 8l5.647 5.646a.5.5 0 01-.708.708l-6-6a.5.5 0 010-.708l6-6a.5.5 0 01.708 0z"/></svg>' +
                '</button>' +
                '<span class="pd-nav-counter" data-pd-nav-counter data-testid="text-nav-counter"></span>' +
                '<button type="button" class="pd-nav-btn" data-pd-nav-next data-testid="button-nav-next" title="Next PIN">' +
                  '<svg width="14" height="14" viewBox="0 0 16 16" fill="currentColor"><path d="M4.646 1.646a.5.5 0 01.708 0l6 6a.5.5 0 010 .708l-6 6a.5.5 0 01-.708-.708L10.293 8 4.646 2.354a.5.5 0 010-.708z"/></svg>' +
                '</button>' +
              '</div>' +
            '</div>' +
          '</div>' +
        '</div>' +
        '<div class="drawer-body" data-testid="drawer-body">' +
          buildDrawerGridHTML(pin) +
        '</div>' +
      '</div>'
    );
  }

  function buildSectionShell(id, title, sourceName, sourceUrl) {
    var sourceLink = '';
    if (sourceName && sourceUrl) {
      sourceLink = '<a href="' + sourceUrl + '" target="_blank" rel="noopener noreferrer" class="pd-source-link" data-testid="link-source-' + id + '">' + escapeHtml(sourceName) + '</a>';
    }
    return (
      '<div class="pd-section loading" id="pd-' + id + '" data-testid="section-' + id + '">' +
        '<div class="pd-section-header">' +
          '<h6>' + escapeHtml(title) + '</h6>' +
          sourceLink +
        '</div>' +
        '<div class="pd-section-body">' +
          '<div class="pd-skeleton w-100" style="margin-bottom:6px;"></div>' +
          '<div class="pd-skeleton w-75" style="margin-bottom:6px;"></div>' +
          '<div class="pd-skeleton w-50"></div>' +
        '</div>' +
      '</div>'
    );
  }

  // ── Data Fetching ──

  function fetchAllSources(pin, signal) {
    var completed = 0;
    var total = 4;

    function checkDone() {
      completed++;
      if (completed >= total) {
        var loader = document.querySelector('[data-pd-global-loading]');
        if (loader) loader.style.display = 'none';
      }
    }

    fetchSource('/api/cook/tax-portal-data', pin, signal, function (resp) {
      try { renderTaxPortal(resp); } catch (e) {
        console.error('PropertyDrawer: renderTaxPortal error', e);
        renderSectionError('tax-portal', 'Render error');
        renderSectionError('characteristics', 'Render error');
        renderSectionError('tax-bills', 'Render error');
        renderSectionError('tax-sale', 'Render error');
      }
      checkDone();
    }, function (err) {
      renderSectionError('tax-portal', err);
      renderSectionError('characteristics', err);
      renderSectionError('tax-bills', err);
      renderSectionError('tax-sale', err);
      checkDone();
    });

    fetchSource('/api/cook/clerk-data', pin, signal, function (resp) {
      try { renderClerk(resp); } catch (e) {
        console.error('PropertyDrawer: renderClerk error', e);
        renderSectionError('sold-taxes', 'Render error');
        renderSectionError('delinquent-taxes', 'Render error');
      }
      checkDone();
    }, function (err) {
      renderSectionError('sold-taxes', err);
      renderSectionError('delinquent-taxes', err);
      checkDone();
    });

    fetchSource('/api/cook/recorder-data', pin, signal, function (resp) {
      try { renderRecorder(resp); } catch (e) {
        console.error('PropertyDrawer: renderRecorder error', e);
        renderSectionError('recorder', 'Render error');
      }
      checkDone();
    }, function (err) {
      renderSectionError('recorder', err);
      checkDone();
    });

    fetchSource('/api/cook/cookviewer-data', pin, signal, function (resp) {
      try { renderCookViewer(resp); } catch (e) {
        console.error('PropertyDrawer: renderCookViewer error', e);
        renderSectionError('gis-map', 'Render error');
        renderSectionError('google-maps', 'Render error');
      }
      checkDone();
    }, function (err) {
      renderSectionError('gis-map', err);
      renderSectionError('google-maps', err);
      checkDone();
    });
  }

  function fetchSource(endpoint, pin, signal, onSuccess, onError) {
    var url = config.apiBaseUrl + endpoint + '?pin=' + encodeURIComponent(pin);
    fetch(url, { signal: signal })
      .then(function (res) { return res.json(); })
      .then(function (json) {
        if (json.success && json.data) {
          var ts = json.cachedAt ? new Date(json.cachedAt) : new Date();
          if (!isNaN(ts.getTime())) {
            state.cachedTimestamps.push(ts);
            updateLastUpdated();
          }
          onSuccess(json.data);
        } else {
          onError(json.error || 'No data available');
        }
      })
      .catch(function (err) {
        if (err.name !== 'AbortError') {
          onError(err.message || 'Network error');
        }
      });
  }

  function formatDateMMDDYYYY(date) {
    var mm = String(date.getMonth() + 1).padStart(2, '0');
    var dd = String(date.getDate()).padStart(2, '0');
    var yyyy = date.getFullYear();
    return mm + '/' + dd + '/' + yyyy;
  }

  function updateLastUpdated() {
    if (state.cachedTimestamps.length === 0) return;
    var oldest = state.cachedTimestamps.reduce(function (a, b) {
      return a < b ? a : b;
    });
    var text = 'Last Updated: ' + formatDateMMDDYYYY(oldest);
    var els = document.querySelectorAll('[data-pd-last-updated]');
    for (var i = 0; i < els.length; i++) {
      els[i].textContent = text;
      els[i].style.display = '';
    }
  }

  // ── Rendering Functions ──

  function renderTaxPortal(data) {
    var section = document.getElementById('pd-tax-portal');
    if (!section) return;
    section.classList.remove('loading');

    var info = data.propertyInfo || {};
    var html = '';

    if (data.propertyImageBase64) {
      html += '<img src="' + sanitizeImageSrc(data.propertyImageBase64) + '" alt="Property" class="pd-property-image" data-testid="img-property-photo">';
    }

    html += '<div class="pd-address-block">';
    html += '<div class="pd-address" data-testid="text-address">' + escapeHtml(info.address || '-') + '</div>';
    html += '<div class="pd-city">' + escapeHtml((info.city || '') + (info.zip ? ', ' + info.zip : '')) + '</div>';
    html += '<div class="pd-meta">Township: ' + escapeHtml(info.township || '-') + '</div>';
    html += '</div>';

    if (info.mailingName) {
      html += '<div class="pd-mailing-block">';
      html += '<div class="pd-mailing-name" data-testid="text-mailing-name">' + escapeHtml(info.mailingName) + '</div>';
      html += '<div class="pd-mailing-addr">' + escapeHtml(info.mailingAddress || '') + '</div>';
      html += '<div class="pd-mailing-addr">' + escapeHtml(info.mailingCityStateZip || '') + '</div>';
      html += '</div>';
    }

    setSectionBody('tax-portal', html);
    renderCharacteristics(data);
    renderTaxBills(data);
    renderTaxSale(data);
  }

  function renderCharacteristics(data) {
    var section = document.getElementById('pd-characteristics');
    if (!section) return;
    section.classList.remove('loading');

    var ch = data.characteristics || {};
    var html = '<div class="pd-chars-grid">';
    html += buildCharRow('Assessed', ch.assessedValue);
    html += buildCharRow('Est. Value', ch.estimatedValue);
    html += buildCharRow('Lot Size', ch.lotSize ? ch.lotSize + ' sqft' : null);
    html += buildCharRow('Building', ch.buildingSize ? ch.buildingSize + ' sqft' : '-');
    html += buildCharRow('Class', ch.propertyClass);
    html += buildCharRow('Tax Rate', ch.taxRate);
    html += buildCharRow('Tax Code', ch.taxCode);
    html += '</div>';

    if (ch.propertyClassDescription) {
      html += '<div class="pd-class-desc">' + escapeHtml(ch.propertyClassDescription) + '</div>';
    }

    setSectionBody('characteristics', html);
  }

  function renderTaxBills(data) {
    var section = document.getElementById('pd-tax-bills');
    if (!section) return;
    section.classList.remove('loading');

    var bills = (data.taxBills && data.taxBills.bills) || [];
    if (bills.length === 0) {
      setSectionBody('tax-bills', '<div class="pd-empty">No tax bill records found</div>');
      return;
    }

    var html = '<div style="overflow-x:auto;">';
    html += '<table class="pd-table" data-testid="table-tax-bills">';
    html += '<thead><tr>';
    html += '<th>Year</th><th class="text-right">Billed</th><th class="text-right">Due</th><th class="text-center">Status</th><th class="text-center">Exmt</th>';
    html += '</tr></thead><tbody>';
    for (var i = 0; i < bills.length; i++) {
      var b = bills[i];
      var dueClass = '';
      if (b.amountDue && b.amountDue !== '-' && b.amountDue !== '$0.00') {
        dueClass = ' text-danger';
      }
      html += '<tr data-testid="row-tax-bill-' + i + '">';
      html += '<td><strong>' + escapeHtml(b.year || '') + '</strong></td>';
      html += '<td class="text-right mono">' + escapeHtml(b.amount || '-') + '</td>';
      html += '<td class="text-right mono' + dueClass + '">' + escapeHtml(b.amountDue || '-') + '</td>';
      html += '<td class="text-center">' + buildStatusBadge(b.paymentStatus) + '</td>';
      html += '<td class="text-center">' + (b.exemptionsReceived || 0) + '</td>';
      html += '</tr>';
    }
    html += '</tbody></table></div>';

    setSectionBody('tax-bills', html);
  }

  function renderTaxSale(data) {
    var section = document.getElementById('pd-tax-sale');
    if (!section) return;
    section.classList.remove('loading');

    var entries = (data.taxSaleDelinquencies && data.taxSaleDelinquencies.entries) || [];
    if (entries.length === 0) {
      setSectionBody('tax-sale', '<div class="pd-empty">No tax sale history</div>');
      return;
    }

    var html = '<div class="pd-tax-sale-row">';
    for (var i = 0; i < entries.length; i++) {
      var e = entries[i];
      html += '<div class="pd-tax-sale-item" data-testid="tax-sale-entry-' + i + '">';
      html += '<span class="year">' + escapeHtml(e.year || '') + ':</span> ';
      html += buildTaxSaleStatusBadge(e.status);
      html += '</div>';
    }
    html += '</div>';

    setSectionBody('tax-sale', html);
  }

  function renderClerk(data) {
    renderSoldTaxes(data);
    renderDelinquentTaxes(data);
  }

  function renderSoldTaxes(data) {
    var section = document.getElementById('pd-sold-taxes');
    if (!section) return;
    section.classList.remove('loading');

    var dt = data.delinquentTaxes || {};
    var sold = dt.soldTaxes || [];

    if (dt.dataAsOf) {
      var hdr = section.querySelector('h6');
      if (hdr) hdr.innerHTML = 'Sold Taxes -- Clerk <span class="pd-as-of">as of ' + escapeHtml(dt.dataAsOf) + '</span>';
    }

    if (sold.length === 0) {
      setSectionBody('sold-taxes', '<div class="pd-empty">No sold taxes found</div>');
      return;
    }

    var html = '<div style="overflow-x:auto;">';
    html += '<table class="pd-table" data-testid="table-sold-taxes">';
    html += '<thead><tr><th>Sale</th><th>Years</th><th>Status</th><th>Doc#</th><th>Date</th></tr></thead>';
    html += '<tbody>';
    for (var i = 0; i < sold.length; i++) {
      var s = sold[i];
      html += '<tr data-testid="row-sold-tax-' + i + '">';
      html += '<td>' + escapeHtml(s.taxSale || '-') + '</td>';
      html += '<td>' + escapeHtml(s.fromYearToYear || '-') + '</td>';
      html += '<td>' + escapeHtml(s.status || '-') + '</td>';
      html += '<td class="mono">' + escapeHtml(s.statusDocNumber || '-') + '</td>';
      html += '<td>' + escapeHtml(s.date || '-') + '</td>';
      html += '</tr>';
    }
    html += '</tbody></table></div>';

    setSectionBody('sold-taxes', html);
  }

  function renderDelinquentTaxes(data) {
    var section = document.getElementById('pd-delinquent-taxes');
    if (!section) return;
    section.classList.remove('loading');

    var dt = data.delinquentTaxes || {};
    var delinquent = dt.delinquentTaxes || [];

    if (delinquent.length === 0) {
      setSectionBody('delinquent-taxes', '<div class="pd-empty">No delinquent taxes found</div>');
      return;
    }

    var html = '<div style="overflow-x:auto;">';
    html += '<table class="pd-table" data-testid="table-delinquent-taxes">';
    html += '<thead><tr><th>Year</th><th>Status</th><th>Forfeit</th><th class="text-right">1st Inst</th><th class="text-right">2nd Inst</th><th>Type</th></tr></thead>';
    html += '<tbody>';
    for (var i = 0; i < delinquent.length; i++) {
      var d = delinquent[i];
      html += '<tr data-testid="row-delinquent-' + i + '">';
      html += '<td><strong>' + escapeHtml(d.taxYear || '-') + '</strong></td>';
      html += '<td>' + escapeHtml(d.status || '-') + '</td>';
      html += '<td>' + escapeHtml(d.forfeitDate || '-') + '</td>';
      html += '<td class="text-right mono">' + escapeHtml(d.firstInstallmentBalance || '-') + '</td>';
      html += '<td class="text-right mono">' + escapeHtml(d.secondInstallmentBalance || '-') + '</td>';
      html += '<td>' + escapeHtml(d.type || '-') + '</td>';
      html += '</tr>';
    }
    html += '</tbody>';

    if (dt.totalTaxBalanceDue1st || dt.totalTaxBalanceDue2nd) {
      html += '<tfoot><tr>';
      html += '<td colspan="3"><strong>Total Due</strong></td>';
      html += '<td class="text-right mono">' + escapeHtml(dt.totalTaxBalanceDue1st || '-') + '</td>';
      html += '<td class="text-right mono">' + escapeHtml(dt.totalTaxBalanceDue2nd || '-') + '</td>';
      html += '<td></td>';
      html += '</tr></tfoot>';
    }

    html += '</table></div>';

    setSectionBody('delinquent-taxes', html);
  }

  function renderRecorder(data) {
    var section = document.getElementById('pd-recorder');
    if (!section) return;
    section.classList.remove('loading');

    var docs = (data.recorderDocuments && data.recorderDocuments.documents) || [];
    var total = (data.recorderDocuments && data.recorderDocuments.totalDocuments) || docs.length;

    var hdr = section.querySelector('h6');
    if (hdr && total > 0) {
      hdr.innerHTML = 'Recorded Documents <span class="pd-badge badge-count">' + total + ' docs</span>';
      var pin14 = state.currentPin ? state.currentPin.replace(/-/g, '') : '';
      hdr.innerHTML += ' <a href="https://crs.cookcountyclerkil.gov/Search/ResultByPin?id1=' + pin14 + '" target="_blank" rel="noopener noreferrer" class="pd-source-link" data-testid="link-source-recorder" title="Open source website">&#x2197;</a>';
    }

    if (docs.length === 0) {
      setSectionBody('recorder', '<div class="pd-empty">No recorded documents found</div>');
      return;
    }

    var html = '<div style="overflow-x:auto;">';
    html += '<table class="pd-table" data-testid="table-recorder-docs">';
    html += '<thead><tr><th>Doc#</th><th>Recorded</th><th>Type</th><th class="text-right">Amount</th></tr></thead>';
    html += '<tbody>';
    for (var i = 0; i < docs.length; i++) {
      var d = docs[i];
      html += '<tr data-testid="row-recorder-doc-' + i + '">';
      html += '<td class="mono">' + escapeHtml(d.docNumber || '-') + '</td>';
      html += '<td>' + escapeHtml(d.dateRecorded || '-') + '</td>';
      html += '<td>' + escapeHtml(d.docType || '-') + '</td>';
      html += '<td class="text-right mono">' + escapeHtml(d.consideration || '-') + '</td>';
      html += '</tr>';
    }
    html += '</tbody></table></div>';

    setSectionBody('recorder', html);
  }

  function renderCookViewer(data) {
    renderGISMap(data);
    renderGoogleMaps(data);
  }

  function renderGISMap(data) {
    var section = document.getElementById('pd-gis-map');
    if (!section) return;
    section.classList.remove('loading');

    var map = data.cookViewerMap || {};

    if (!map.mapImageBase64) {
      setSectionBody('gis-map', '<div class="pd-empty">GIS map unavailable</div>');
      return;
    }

    var html = '<div class="pd-map-container" data-testid="gis-map-container" style="aspect-ratio:' + (map.mapWidth || 400) + '/' + (map.mapHeight || 300) + ';">';
    html += '<img src="' + sanitizeImageSrc(map.mapImageBase64) + '" alt="Satellite map" data-testid="img-gis-map">';

    if (map.parcelOverlayBase64) {
      html += '<img src="' + sanitizeImageSrc(map.parcelOverlayBase64) + '" alt="Parcel boundaries" class="pd-map-overlay" data-testid="img-parcel-overlay">';
    }

    if (map.parcelRingsWebMercator && map.parcelRingsWebMercator.length > 0 && map.mapBbox && map.mapBbox.length === 4) {
      var mw = map.mapWidth || 400;
      var mh = map.mapHeight || 300;
      var bbox = map.mapBbox;
      var bboxW = bbox[2] - bbox[0];
      var bboxH = bbox[3] - bbox[1];

      html += '<svg viewBox="0 0 ' + mw + ' ' + mh + '" preserveAspectRatio="none" data-testid="svg-parcel-overlay">';
      for (var r = 0; r < map.parcelRingsWebMercator.length; r++) {
        var ring = map.parcelRingsWebMercator[r];
        var points = [];
        for (var p = 0; p < ring.length; p++) {
          var sx = ((ring[p][0] - bbox[0]) / bboxW) * mw;
          var sy = ((bbox[3] - ring[p][1]) / bboxH) * mh;
          points.push(sx + ',' + sy);
        }
        html += '<polygon points="' + points.join(' ') + '" fill="rgba(59,130,246,0.15)" stroke="#2563eb" stroke-width="1.5" stroke-linejoin="miter" />';
      }
      html += '</svg>';
    }

    html += '</div>';

    if (map.centerLat && map.centerLon) {
      html += '<div class="pd-map-coords" data-testid="text-map-coords">' + map.centerLat.toFixed(6) + ', ' + map.centerLon.toFixed(6) + '</div>';
    }

    setSectionBody('gis-map', html);
  }

  function renderGoogleMaps(data) {
    var section = document.getElementById('pd-google-maps');
    if (!section) return;
    section.classList.remove('loading');

    var map = data.cookViewerMap || {};
    var hasSat = !!map.googleSatelliteImageBase64;
    var hasSV = !!map.googleStreetViewImageBase64;

    if (!hasSat && !hasSV) {
      setSectionBody('google-maps', '<div class="pd-empty">Google Maps imagery unavailable</div>');
      return;
    }

    var html = '<div class="pd-google-maps">';
    if (hasSat) {
      html += '<div class="pd-google-map-item">';
      html += '<div class="pd-map-label">Satellite</div>';
      html += '<img src="' + sanitizeImageSrc(map.googleSatelliteImageBase64) + '" alt="Google Satellite" data-testid="img-google-satellite">';
      html += '</div>';
    }
    if (hasSV) {
      html += '<div class="pd-google-map-item">';
      html += '<div class="pd-map-label">Street View</div>';
      html += '<img src="' + sanitizeImageSrc(map.googleStreetViewImageBase64) + '" alt="Google Street View" data-testid="img-google-streetview">';
      html += '</div>';
    }
    html += '</div>';

    setSectionBody('google-maps', html);
  }

  // ── Helper Functions ──

  function setSectionBody(sectionId, html) {
    var section = document.getElementById('pd-' + sectionId);
    if (!section) return;
    var body = section.querySelector('.pd-section-body') || section.querySelector('.pd-card-body');
    if (body) body.innerHTML = html;
  }

  function renderSectionError(sectionId, message) {
    var section = document.getElementById('pd-' + sectionId);
    if (!section) return;
    section.classList.remove('loading');
    var body = section.querySelector('.pd-section-body') || section.querySelector('.pd-card-body');
    if (body) {
      body.innerHTML = '<div class="pd-error" data-testid="error-' + sectionId + '">' +
        '<svg width="16" height="16" viewBox="0 0 16 16" fill="currentColor"><path d="M8 1a7 7 0 100 14A7 7 0 008 1zM7 5h2v4H7V5zm0 5h2v2H7v-2z"/></svg>' +
        '<span>' + escapeHtml(message || 'Failed to load') + '</span>' +
      '</div>';
    }
  }

  function buildCharRow(label, value) {
    return '<div class="pd-char-row"><span class="pd-char-label">' + escapeHtml(label) + ':</span> <span class="pd-char-value">' + escapeHtml(value || '-') + '</span></div>';
  }

  function buildKV(label, value, extraClass) {
    return '<div class="pd-kv">' +
      '<span class="pd-kv-label">' + escapeHtml(label) + '</span>' +
      '<span class="pd-kv-value' + (extraClass ? ' ' + extraClass : '') + '">' + escapeHtml(value || '-') + '</span>' +
    '</div>';
  }

  function buildStatusBadge(status) {
    if (!status) return '-';
    var lower = status.toLowerCase();
    var cls = 'status-pending';
    var label = status;
    if (lower.indexOf('paid') !== -1) {
      cls = 'status-paid';
      label = 'Paid';
    } else if (lower.indexOf('balance') !== -1 || lower.indexOf('due') !== -1) {
      cls = 'status-due';
      label = 'Due';
    } else if (lower.indexOf('payment history') !== -1) {
      cls = 'status-info';
      label = 'Payment History';
    }
    return '<span class="pd-status ' + cls + '">' + escapeHtml(label) + '</span>';
  }

  function buildTaxSaleStatusBadge(status) {
    if (!status) return '-';
    var lower = status.toLowerCase();
    var cls = 'status-info';
    if (lower.indexOf('sold') !== -1) cls = 'status-sold';
    else if (lower.indexOf('pending') !== -1) cls = 'status-pending';
    else if (lower.indexOf('not occurred') !== -1 || lower.indexOf('no sale') !== -1 || lower.indexOf('no tax sale') !== -1) cls = 'status-no-sale';
    return '<span class="pd-status ' + cls + '">' + escapeHtml(status) + '</span>';
  }

  function escapeHtml(str) {
    if (str == null) return '';
    return String(str)
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;')
      .replace(/'/g, '&#39;');
  }

  function sanitizeImageSrc(src) {
    if (!src || typeof src !== 'string') return '';
    if (src.indexOf('data:image/') === 0) return src;
    if (src.indexOf('https://') === 0) return src;
    return '';
  }

  // ── Keyboard Navigation ──
  document.addEventListener('keydown', function (e) {
    if (!state.currentPin || config.pinList.length === 0) return;
    var tag = (e.target.tagName || '').toLowerCase();
    if (tag === 'input' || tag === 'textarea' || tag === 'select') return;
    if (e.target.isContentEditable) return;
    if (e.key === 'ArrowLeft') { e.preventDefault(); navigatePrev(); }
    else if (e.key === 'ArrowRight') { e.preventDefault(); navigateNext(); }
  });

  // ── Public API ──
  return {
    configure: configure,
    open: open,
    close: close,
    search: search,
    setPinList: setPinList,
    navigatePrev: navigatePrev,
    navigateNext: navigateNext,
    version: '2.2.0',
  };
})();
