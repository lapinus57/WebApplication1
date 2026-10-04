const CACHE = 'eyechat-mobile-v3';
const SHELL = ['/css/site.css', '/js/mobile.js'];

self.addEventListener('install', event => event.waitUntil(caches.open(CACHE).then(cache => cache.addAll(SHELL))));
self.addEventListener('activate', event => event.waitUntil(caches.keys().then(keys => Promise.all(keys.filter(key => key !== CACHE).map(key => caches.delete(key))))));
self.addEventListener('fetch', event => {
  if (event.request.method !== 'GET' || event.request.mode === 'navigate' || new URL(event.request.url).search) return;
  event.respondWith(caches.match(event.request).then(cached => cached || fetch(event.request)));
});
