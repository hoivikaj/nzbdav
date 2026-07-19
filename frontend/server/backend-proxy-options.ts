// httpxy's inbound `timeout` option is deliberately omitted: it calls
// req.socket.setTimeout(ms, cb) per proxied request, stacking 'timeout'
// listeners on keep-alive client sockets (issue #486). Inbound long-request
// protection lives on the HTTP server (requestTimeout/headersTimeout).
//
// proxyTimeout aborts hung backend upstream requests and is safe: Node clears
// the outbound ClientRequest socket listener when the request completes.
export const LONG_RUNNING_PROXY_TIMEOUT_MS = 3 * 60 * 60 * 1000; // 3 hours

export const backendProxyTimeoutOptions = {
  proxyTimeout: LONG_RUNNING_PROXY_TIMEOUT_MS,
} as const;
