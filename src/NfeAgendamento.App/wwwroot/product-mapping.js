const NfeProductMapping = (() => {
  const FERNANDO_KLEIN_TAX_ID = '06727793905';

  const FERNANDO_KLEIN_CATALOG = Object.freeze([
    Object.freeze({ internalCode: '73457', name: 'ALFACE CRESPA', aliases: Object.freeze(['ALFACE', 'ALFACE CRESPA']) }),
    Object.freeze({ internalCode: '104128', name: 'ALFACE LISA', aliases: Object.freeze(['ALFACE LISA']) }),
    Object.freeze({ internalCode: '104129', name: 'ALFACE ROXA', aliases: Object.freeze(['ALFACE ROXA']) }),
    Object.freeze({ internalCode: '30228', name: 'ALFACE AMERICANA', aliases: Object.freeze(['ALFACE AMERICANA', 'AMERICANA']) }),
    Object.freeze({ internalCode: '104130', name: 'ALFAVACA', aliases: Object.freeze(['ALFAVACA']) }),
    Object.freeze({ internalCode: '104109', name: 'AGRIAO', aliases: Object.freeze(['AGRIAO']) }),
    Object.freeze({ internalCode: '104108', name: 'BROCOLIS', aliases: Object.freeze(['BROCOLIS']) }),
    Object.freeze({ internalCode: '104106', name: 'CEBOLINHA', aliases: Object.freeze(['CEBOLA', 'CEBOLINHA']) }),
    Object.freeze({ internalCode: '104113', name: 'COENTRO', aliases: Object.freeze(['COENTRO']) }),
    Object.freeze({ internalCode: '104107', name: 'COUVE', aliases: Object.freeze(['COUVE']) }),
    Object.freeze({ internalCode: '104104', name: 'CHICORIA', aliases: Object.freeze(['CHICORIA']) }),
    Object.freeze({ internalCode: '104110', name: 'ESPINAFRE', aliases: Object.freeze(['ESPINAFRE']) }),
    Object.freeze({ internalCode: '104115', name: 'HORTELA', aliases: Object.freeze(['HORTELA']) }),
    Object.freeze({ internalCode: '104114', name: 'MANJERICAO', aliases: Object.freeze(['MANJERICAO']) }),
    Object.freeze({ internalCode: '104111', name: 'RUCULA', aliases: Object.freeze(['RUCULA']) }),
    Object.freeze({ internalCode: '104112', name: 'RADITE', aliases: Object.freeze(['RADITE']) }),
    Object.freeze({ internalCode: '104105', name: 'SALSINHA', aliases: Object.freeze(['SALSA', 'SALSINHA']) })
  ]);

  function normalizeTaxId(value) {
    return String(value || '')
      .toUpperCase()
      .replace(/[^A-Z0-9]/g, '');
  }

  function normalizeProductName(value) {
    const normalized = String(value || '')
      .trim()
      .normalize('NFD')
      .replace(/[\u0300-\u036f]/g, '')
      .toUpperCase()
      .replace(/[^A-Z0-9]+/g, ' ')
      .trim()
      .replace(/\s+/g, ' ');

    return normalized.replace(/^VERDURAS(?:\s+|$)/, '').trim();
  }

  function buildAliasIndex(catalog) {
    const aliases = {};

    for (const item of catalog || []) {
      const internalCode = String(item?.internalCode || '').trim();
      if (!internalCode) throw new Error('Produto do catálogo sem código interno.');

      for (const rawAlias of item?.aliases || []) {
        const alias = normalizeProductName(rawAlias);
        if (!alias) throw new Error(`Alias vazio no código interno ${internalCode}.`);

        const existingCode = aliases[alias];
        if (existingCode && existingCode !== internalCode) {
          throw new Error(`Alias conflitante ${alias}: códigos internos ${existingCode} e ${internalCode}.`);
        }

        aliases[alias] = internalCode;
      }
    }

    return Object.freeze(aliases);
  }

  function validateCatalog(catalog) {
    buildAliasIndex(catalog);
    return true;
  }

  const FERNANDO_KLEIN_ALIAS_INDEX = buildAliasIndex(FERNANDO_KLEIN_CATALOG);

  function resolveFernandoKleinProduct({ emitterTaxId, xProd, cProd }) {
    const sourceCode = String(cProd || '');
    if (normalizeTaxId(emitterTaxId) !== FERNANDO_KLEIN_TAX_ID) {
      return { sourceCode, internalCode: '' };
    }

    const productName = normalizeProductName(xProd);
    return {
      sourceCode,
      internalCode: FERNANDO_KLEIN_ALIAS_INDEX[productName] || ''
    };
  }

  return Object.freeze({
    normalizeTaxId,
    normalizeProductName,
    resolveFernandoKleinProduct,
    validateCatalog
  });
})();
