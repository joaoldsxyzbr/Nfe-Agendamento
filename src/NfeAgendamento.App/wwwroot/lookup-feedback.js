(function (root, factory) {
  const api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  root.NfeLookupFeedback = api;
})(typeof globalThis !== 'undefined' ? globalThis : this, function () {
  function parseRetryAfterSeconds(value) {
    if (value === null || value === undefined || value === '') return null;
    const seconds = Number.parseInt(String(value), 10);
    return Number.isFinite(seconds) && seconds > 0 ? seconds : null;
  }

  function buildLookupErrorMessage({ statusCode, error, retryAfter }) {
    const payload = error || {};

    if (statusCode === 429 && payload.status === 'fila_ocupada') {
      const seconds = parseRetryAfterSeconds(retryAfter) || 5;
      return `A Central está ocupada. Tente novamente em ${seconds} segundos.`;
    }

    if (statusCode === 429 && payload.status === 'consumo_indevido' && payload.blockedUntilUtc) {
      const until = new Date(payload.blockedUntilUtc);
      if (!Number.isNaN(until.getTime())) {
        return `Consultas bloqueadas pela SEFAZ até ${until.toLocaleString('pt-BR')}. Não repita a consulta antes desse horário.`;
      }
    }

    return payload.message || 'Falha na consulta.';
  }

  return { buildLookupErrorMessage, parseRetryAfterSeconds };
});
