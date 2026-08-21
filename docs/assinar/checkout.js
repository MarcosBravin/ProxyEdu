(() => {
  'use strict';

  const API_BASE = 'https://pxeapi.bravintech.com';
  const CHECKOUT_HOSTS = new Set(['mercadopago.com.br', 'www.mercadopago.com.br']);
  const checkoutEnabled = document.body.dataset.checkoutEnabled === 'true';
  const byId = (id) => document.getElementById(id);
  const planGrid = byId('commercial-plan-grid');
  const catalogStatus = byId('catalog-status');
  const form = byId('commercial-checkout-form');
  const submit = byId('commercial-submit');
  const status = byId('commercial-status');

  if (!planGrid || !catalogStatus || !form || !submit || !status) return;

  const fallbackPlans = [
    { sku: 'LAB15_MONTHLY', planCode: 'LAB15', displayName: 'Lab 15', billingCycle: 'Monthly', price: 69, currency: 'BRL', maxDevices: 15, maxServers: 1, features: ['Operação local', 'Cache de licença assinado'] },
    { sku: 'LAB15_ANNUAL', planCode: 'LAB15', displayName: 'Lab 15', billingCycle: 'Annual', price: 690, currency: 'BRL', maxDevices: 15, maxServers: 1, features: ['Operação local', 'Cache de licença assinado'] },
    { sku: 'SCHOOL40_MONTHLY', planCode: 'SCHOOL40', displayName: 'Escola 40', billingCycle: 'Monthly', price: 149, currency: 'BRL', maxDevices: 40, maxServers: 1, features: ['Gestão centralizada de seats', 'Operação local'] },
    { sku: 'SCHOOL40_ANNUAL', planCode: 'SCHOOL40', displayName: 'Escola 40', billingCycle: 'Annual', price: 1490, currency: 'BRL', maxDevices: 40, maxServers: 1, features: ['Gestão centralizada de seats', 'Operação local'] },
    { sku: 'SCHOOL80_MONTHLY', planCode: 'SCHOOL80', displayName: 'Escola 80', billingCycle: 'Monthly', price: 249, currency: 'BRL', maxDevices: 80, maxServers: 1, features: ['Validação resiliente', 'Operação local'] },
    { sku: 'SCHOOL80_ANNUAL', planCode: 'SCHOOL80', displayName: 'Escola 80', billingCycle: 'Annual', price: 2490, currency: 'BRL', maxDevices: 80, maxServers: 1, features: ['Validação resiliente', 'Operação local'] },
    { sku: 'SCHOOL150_MONTHLY', planCode: 'SCHOOL150', displayName: 'Escola 150', billingCycle: 'Monthly', price: 399, currency: 'BRL', maxDevices: 150, maxServers: 1, features: ['Auditoria comercial', 'Operação local'] },
    { sku: 'SCHOOL150_ANNUAL', planCode: 'SCHOOL150', displayName: 'Escola 150', billingCycle: 'Annual', price: 3990, currency: 'BRL', maxDevices: 150, maxServers: 1, features: ['Auditoria comercial', 'Operação local'] }
  ];

  let plans = fallbackPlans;
  let billingCycle = 'Monthly';
  let selectedPlan = fallbackPlans[0];
  let activeRequestId = null;
  let catalogConfirmed = false;

  const money = (value) => new Intl.NumberFormat('pt-BR', {
    style: 'currency', currency: 'BRL', minimumFractionDigits: 2
  }).format(value);

  const validPlan = (plan) => plan &&
    typeof plan.sku === 'string' && /^[A-Z0-9]+_(MONTHLY|ANNUAL)$/.test(plan.sku) &&
    typeof plan.displayName === 'string' && plan.displayName.length > 0 && plan.displayName.length <= 100 &&
    ['Monthly', 'Annual'].includes(plan.billingCycle) &&
    Number.isFinite(Number(plan.price)) && Number(plan.price) > 0 &&
    plan.currency === 'BRL' && Number.isInteger(Number(plan.maxDevices)) && Number(plan.maxDevices) > 0;

  const setStatus = (message, type = '') => {
    status.className = `trial-status${type ? ` ${type}` : ''}`;
    status.textContent = message;
  };

  const refreshSubmitState = () => {
    submit.disabled = !(checkoutEnabled && catalogConfirmed);
    submit.textContent = !checkoutEnabled
      ? 'Compra online em preparação'
      : catalogConfirmed
        ? 'Continuar para o Mercado Pago'
        : 'Aguardando confirmação do catálogo';
  };

  const updateSelectedSummary = () => {
    byId('selected-plan-name').textContent = `${selectedPlan.displayName} — ${selectedPlan.billingCycle === 'Annual' ? 'Anual' : 'Mensal'}`;
    byId('selected-plan-price').textContent = money(Number(selectedPlan.price));
    byId('selected-plan-cycle').textContent = selectedPlan.billingCycle === 'Annual' ? 'por ano' : 'por mês';
  };

  const selectPlan = (sku, shouldFocus = true) => {
    const plan = plans.find((candidate) => candidate.sku === sku && candidate.billingCycle === billingCycle);
    if (!plan) return;
    selectedPlan = plan;
    updateSelectedSummary();
    planGrid.querySelectorAll('.commercial-plan-card').forEach((card) => {
      const selected = card.dataset.sku === sku;
      card.classList.toggle('selected', selected);
      card.querySelector('button')?.setAttribute('aria-pressed', String(selected));
    });
    if (shouldFocus) {
      byId('checkout-form-title')?.scrollIntoView({ behavior: 'smooth', block: 'start' });
    }
  };

  const renderPlans = () => {
    planGrid.replaceChildren();
    const visible = plans.filter((plan) => plan.billingCycle === billingCycle)
      .sort((a, b) => Number(a.maxDevices) - Number(b.maxDevices));
    visible.forEach((plan, index) => {
      const card = document.createElement('article');
      card.className = `commercial-plan-card${index === 0 ? ' featured' : ''}`;
      card.dataset.sku = plan.sku;

      const capacity = document.createElement('span');
      capacity.className = 'commercial-plan-capacity';
      capacity.textContent = `ATÉ ${Number(plan.maxDevices)} DISPOSITIVOS`;
      const name = document.createElement('h3');
      name.textContent = plan.displayName;
      const price = document.createElement('p');
      price.className = 'commercial-plan-price';
      const priceStrong = document.createElement('strong');
      priceStrong.textContent = money(Number(plan.price));
      const cycle = document.createElement('small');
      cycle.textContent = plan.billingCycle === 'Annual' ? '/ano' : '/mês';
      price.append(priceStrong, cycle);
      const description = document.createElement('p');
      description.className = 'commercial-plan-description';
      description.textContent = Number(plan.maxDevices) <= 15
        ? 'Para um laboratório compacto ou sala dedicada.'
        : `Para ambientes com até ${Number(plan.maxDevices)} estações gerenciadas.`;
      const features = document.createElement('ul');
      const featureLabels = Array.isArray(plan.features) ? plan.features.slice(0, 3) : [];
      [`${Number(plan.maxDevices)} seats de dispositivo`, ...featureLabels].forEach((label) => {
        const item = document.createElement('li');
        item.textContent = String(label);
        features.append(item);
      });
      const button = document.createElement('button');
      button.type = 'button';
      button.className = 'commercial-plan-select';
      button.textContent = 'Selecionar plano';
      button.setAttribute('aria-pressed', 'false');
      button.addEventListener('click', () => selectPlan(plan.sku));
      card.append(capacity, name, price, description, features, button);
      planGrid.append(card);
    });

    const equivalent = visible.find((plan) => plan.planCode === selectedPlan.planCode) || visible[0];
    if (equivalent) selectPlan(equivalent.sku, false);
  };

  const allowedCheckoutUrl = (value) => {
    try {
      const url = new URL(value);
      return url.protocol === 'https:' && url.port === '' && CHECKOUT_HOSTS.has(url.hostname);
    } catch {
      return false;
    }
  };

  const parseJson = async (response) => response.headers.get('content-type')?.includes('json')
    ? response.json()
    : null;

  const redirectToCheckout = (result) => {
    if (!result || result.status !== 'CheckoutCreated' || !allowedCheckoutUrl(result.checkoutUrl)) {
      throw new Error('O provedor não retornou um endereço de pagamento válido. Nenhum redirecionamento foi realizado.');
    }
    window.location.assign(result.checkoutUrl);
  };

  const pollCheckout = async (publicId) => {
    for (let attempt = 0; attempt < 6; attempt += 1) {
      await new Promise((resolve) => window.setTimeout(resolve, 5000));
      const response = await fetch(`${API_BASE}/api/public/v1/commercial/checkouts/${encodeURIComponent(publicId)}`, {
        method: 'GET', headers: { Accept: 'application/json' }, cache: 'no-store'
      });
      const result = await parseJson(response);
      if (!response.ok) throw new Error(result?.message || 'Não foi possível consultar a contratação.');
      if (result?.status === 'CheckoutCreated') return redirectToCheckout(result);
      if (['Paid', 'PendingPayment'].includes(result?.status)) {
        setStatus('Pagamento recebido. Aguarde o e-mail com a licença e as instruções de ativação.', 'success');
        return;
      }
      if (['Failed', 'Canceled', 'Refunded', 'Chargeback'].includes(result?.status)) {
        throw new Error('A contratação não foi concluída. Nenhuma nova tentativa automática foi feita.');
      }
    }
    setStatus(`A confirmação ainda está em processamento. Guarde o protocolo ${publicId} e não crie outro pedido.`, 'error');
  };

  document.querySelectorAll('[data-billing]').forEach((button) => {
    button.addEventListener('click', () => {
      billingCycle = button.dataset.billing;
      document.querySelectorAll('[data-billing]').forEach((candidate) => {
        candidate.setAttribute('aria-pressed', String(candidate === button));
      });
      renderPlans();
    });
  });

  byId('change-plan')?.addEventListener('click', () => {
    byId('commercial-plans-title')?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  });

  form.addEventListener('submit', async (event) => {
    event.preventDefault();
    setStatus('');
    if (!checkoutEnabled) {
      setStatus('A contratação online ainda não foi liberada. Solicite um Trial enquanto concluímos a preparação.', 'error');
      return;
    }
    if (!catalogConfirmed) {
      setStatus('O catálogo não pôde ser confirmado. Nenhum pagamento foi iniciado.', 'error');
      return;
    }
    if (!form.reportValidity()) return;
    if (!window.crypto?.randomUUID) {
      setStatus('Este navegador não oferece os recursos de segurança necessários. Atualize-o antes de continuar.', 'error');
      return;
    }

    submit.disabled = true;
    submit.textContent = 'Preparando checkout…';
    activeRequestId ||= window.crypto.randomUUID();
    const data = new FormData(form);
    const payload = {
      requestId: activeRequestId,
      sku: selectedPlan.sku,
      customerName: String(data.get('customerName') || '').trim(),
      organizationName: String(data.get('organizationName') || '').trim(),
      contactEmail: String(data.get('contactEmail') || '').trim(),
      privacyAccepted: data.get('privacyAccepted') === 'on',
      website: String(data.get('website') || '')
    };

    try {
      const response = await fetch(`${API_BASE}/api/public/v1/commercial/checkouts`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
        body: JSON.stringify(payload)
      });
      const result = await parseJson(response);
      if (response.status === 202 && result?.publicId) {
        setStatus(`O provedor ainda está processando. Não feche nem envie novamente. Protocolo ${result.publicId}.`);
        await pollCheckout(result.publicId);
        return;
      }
      if (!response.ok) {
        if (response.status < 500) activeRequestId = null;
        const message = response.status === 429
          ? 'Limite temporário de tentativas atingido. Aguarde antes de tentar novamente.'
          : response.status === 503
            ? 'A contratação online ainda não está disponível.'
            : result?.message || 'Não foi possível iniciar a contratação.';
        throw new Error(message);
      }
      redirectToCheckout(result);
    } catch (error) {
      const reference = activeRequestId ? ` Protocolo ${activeRequestId}. Não crie outro pedido.` : '';
      setStatus(`${error?.message || 'Não foi possível confirmar o resultado.'}${reference}`, 'error');
    } finally {
      payload.contactEmail = '';
      payload.customerName = '';
      payload.organizationName = '';
      refreshSubmitState();
    }
  });

  refreshSubmitState();
  renderPlans();
  fetch(`${API_BASE}/api/public/v1/commercial/plans`, {
    method: 'GET', headers: { Accept: 'application/json' }
  })
    .then(async (response) => {
      if (!response.ok) throw new Error(`Catálogo indisponível (${response.status})`);
      return response.json();
    })
    .then((result) => {
      const received = Array.isArray(result?.plans) ? result.plans.filter(validPlan) : [];
      const uniqueSkus = new Set(received.map((plan) => plan.sku));
      if (received.length !== 8 || uniqueSkus.size !== 8) throw new Error('Catálogo comercial incompleto');
      plans = received;
      catalogConfirmed = true;
      catalogStatus.textContent = 'Valores confirmados pelo catálogo comercial do ProxyEdu.';
      catalogStatus.className = 'catalog-status success';
      renderPlans();
      refreshSubmitState();
    })
    .catch(() => {
      catalogConfirmed = false;
      catalogStatus.textContent = 'Exibindo valores de referência. A cobrança continua bloqueada até o catálogo ser confirmado.';
      catalogStatus.className = 'catalog-status warning';
      refreshSubmitState();
    });
})();
