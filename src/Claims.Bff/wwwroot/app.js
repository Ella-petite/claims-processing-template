const state = { claims: [], selectedId: null };
const stages = ['Customer validated', 'Policy validated', 'Fraud check', 'Manual review', 'Payment', 'Completed'];
const money = new Intl.NumberFormat('en-ZA', { style: 'currency', currency: 'ZAR', maximumFractionDigits: 0 });
const date = new Intl.DateTimeFormat('en-ZA', { day: '2-digit', month: 'short', year: 'numeric' });

async function loadClaims() {
  const response = await fetch('/api/client/claims');
  if (!response.ok) throw new Error('Claims API unavailable');
  state.claims = await response.json();
  if (!state.selectedId && state.claims.length) state.selectedId = state.claims.find(claim => claim.claimReference === 'CL-002')?.id ?? state.claims[0].id;
  renderMetrics();
  renderList();
  renderDetail();
}

function renderMetrics() {
  const open = state.claims.filter(claim => !['Paid', 'Rejected'].includes(statusName(claim.status))).length;
  const review = state.claims.filter(claim => statusName(claim.status) === 'UnderReview').length;
  const paid = state.claims.filter(claim => statusName(claim.status) === 'Paid').reduce((sum, claim) => sum + claim.amount, 0);
  const exposure = state.claims.reduce((sum, claim) => sum + claim.amount, 0);
  document.querySelector('#metric-open').textContent = open;
  document.querySelector('#metric-review').textContent = review;
  document.querySelector('#metric-paid').textContent = money.format(paid);
  document.querySelector('#metric-exposure').textContent = money.format(exposure);
}

function renderList() {
  const query = document.querySelector('#search-input').value.toLowerCase();
  const claims = state.claims.filter(claim => [claim.claimReference, claim.clientId, claim.policyId, claim.claimType].some(value => value.toLowerCase().includes(query)));
  const list = document.querySelector('#claim-list');
  list.innerHTML = claims.length ? claims.map(claim => `
    <article class="claim-card ${claim.id === state.selectedId ? 'selected' : ''}" data-id="${claim.id}">
      <div class="claim-card-top"><span class="claim-ref">${claim.claimReference}</span><span class="claim-amount">${money.format(claim.amount)}</span></div>
      <div class="claim-client">${claim.clientId} · ${claim.claimType}</div>
      <div class="claim-card-bottom"><span class="date">${date.format(new Date(claim.incidentDate))}</span><span class="status ${statusClass(claim.status)}">${statusLabel(claim.status)}</span></div>
    </article>`).join('') : '<div class="empty-list">No claims match your search.</div>';
  list.querySelectorAll('.claim-card').forEach(card => card.addEventListener('click', () => { state.selectedId = card.dataset.id; renderList(); renderDetail(); }));
}

function renderDetail() {
  const claim = state.claims.find(item => item.id === state.selectedId);
  const detail = document.querySelector('#detail-panel');
  if (!claim) return;
  const claimStatus = statusName(claim.status);
  const historyNotes = claim.history.map(item => item.note || '').join(' ').toLowerCase();
  const checks = [historyNotes.includes('customer validated'), historyNotes.includes('policy validated'), historyNotes.includes('manual review required') || historyNotes.includes('fraud'), claimStatus === 'UnderReview', ['PaymentPending', 'Paid'].includes(claimStatus), claimStatus === 'Paid'];
  const stageMarkup = stages.map((stage, index) => `<div class="stage ${checks[index] && index !== 3 ? 'done' : ''} ${index === 3 && claimStatus === 'UnderReview' ? 'current' : ''}"><span class="stage-dot">${checks[index] && !(index === 3 && claimStatus === 'UnderReview') ? '✓' : index === 3 && claimStatus === 'UnderReview' ? '!' : ''}</span><span class="stage-name">${stage}</span></div>`).join('');
  detail.innerHTML = `
    <div class="detail-header"><div class="detail-title"><div><h2>${claim.claimReference}</h2><p>${claim.description}</p></div></div><span class="status ${statusClass(claimStatus)}">${statusLabel(claimStatus)}</span></div>
    <div class="detail-grid"><div class="field"><label>Client</label><strong>${claim.clientId}</strong></div><div class="field"><label>Policy</label><strong>${claim.policyId}</strong></div><div class="field"><label>Claim type</label><strong>${claim.claimType}</strong></div><div class="field"><label>Amount</label><strong>${money.format(claim.amount)}</strong></div></div>
    <section class="workflow-section"><h3 class="section-title">Workflow</h3><div class="timeline">${stageMarkup}</div></section>
    <section class="documents-section"><h3 class="section-title">Documents</h3><div class="documents">${claim.documentReferences.map(document => `<span class="document">${document}</span>`).join('')}</div></section>
    <div class="actions"><p>${claimStatus === 'UnderReview' ? 'Analyst decision required to continue this claim.' : 'Claim activity is synchronised across connected systems.'}</p><div class="action-buttons"><button class="action-button reject" data-decision="false" ${claimStatus !== 'UnderReview' ? 'disabled' : ''}>Reject claim</button><button class="action-button approve" data-decision="true" ${claimStatus !== 'UnderReview' ? 'disabled' : ''}>Approve claim</button></div></div>`;
  detail.querySelectorAll('[data-decision]').forEach(button => button.addEventListener('click', () => decideClaim(claim.id, button.dataset.decision === 'true')));
}

async function decideClaim(id, approved) {
  const buttons = document.querySelectorAll('[data-decision]');
  buttons.forEach(button => button.disabled = true);
  const response = await fetch(`/api/client/claims/${id}/decision`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ approved }) });
  if (!response.ok) { showToast('Could not update the claim'); buttons.forEach(button => button.disabled = false); return; }
  showToast(approved ? 'Claim approved and workflow updated' : 'Claim rejected and workflow updated');
  await loadClaims();
}

function statusName(status) { return typeof status === 'number' ? ['Submitted', 'ValidatingCustomer', 'ValidatingPolicy', 'UnderReview', 'Approved', 'PaymentPending', 'Paid', 'Rejected', 'Failed'][status] : status; }
function statusLabel(status) { return ({ UnderReview: 'Manual review', PaymentPending: 'Payment pending', Paid: 'Paid', Rejected: 'Rejected', Approved: 'Approved', Submitted: 'Submitted' })[status] || status; }
function statusClass(status) { return ({ UnderReview: 'review', PaymentPending: 'payment', Paid: 'paid', Rejected: 'rejected', Approved: 'approved', Submitted: 'submitted' })[status] || 'submitted'; }
function showToast(message) { const toast = document.querySelector('#toast'); toast.textContent = message; toast.classList.add('show'); setTimeout(() => toast.classList.remove('show'), 3000); }

document.querySelector('#search-input').addEventListener('input', renderList);
document.querySelector('#refresh-button').addEventListener('click', () => loadClaims().then(() => showToast('Claims refreshed')));
loadClaims().catch(() => { document.querySelector('#claim-list').innerHTML = '<div class="empty-list">Claims API is unavailable. Start the Claims API and refresh.</div>'; });
