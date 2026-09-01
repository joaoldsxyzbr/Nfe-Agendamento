const NfeProductMapping = (() => {
  const FERNANDO_KLEIN_TAX_ID = '06727793905';

  const FERNANDO_KLEIN_PRODUCT_CODES = Object.freeze({
    'ALFACE': '73457',
    'ALFACE CRESPA': '73457',
    'ALFACE LISA': '104128',
    'ALFACE ROXA': '104129',
    'ALFACE AMERICANA': '30228',
    'AMERICANA': '30228',
    'ALFAVACA': '104130',
    'AGRIAO': '104109',
    'BROCOLIS': '104108',
    'CEBOLA': '104106',
    'CEBOLINHA': '104106',
    'COENTRO': '104113',
    'COUVE': '104107',
    'CHICORIA': '104104',
    'ESPINAFRE': '104110',
    'HORTELA': '104115',
    'MANJERICAO': '104114',
    'RUCULA': '104111',
    'RADITE': '104112',
    'SALSA': '104105',
    'SALSINHA': '104105'
  });

  function normalizeTaxId(value) {
    return String(value || '')
      .toUpperCase()
      .replace(/[^A-Z0-9]/g, '');
  }

  function normalizeProductName(value) {
    return String(value || '')
      .trim()
      .normalize('NFD')
      .replace(/[\u0300-\u036f]/g, '')
      .toUpperCase()
      .replace(/^VERDURAS\s*[-:]\s*/, '')
      .replace(/[^A-Z0-9]+/g, ' ')
      .trim()
      .replace(/\s+/g, ' ');
  }

  function resolveFernandoKleinProduct({ emitterTaxId, xProd, cProd }) {
    const sourceCode = String(cProd || '');
    if (normalizeTaxId(emitterTaxId) !== FERNANDO_KLEIN_TAX_ID) {
      return { sourceCode, internalCode: '' };
    }

    const productName = normalizeProductName(xProd);
    return {
      sourceCode,
      internalCode: FERNANDO_KLEIN_PRODUCT_CODES[productName] || ''
    };
  }

  return Object.freeze({
    normalizeTaxId,
    normalizeProductName,
    resolveFernandoKleinProduct
  });
})();
