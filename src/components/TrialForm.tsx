import { useEffect, useState } from "react";
import type { SubmitEvent } from "react";

const endpoint = "https://pxeapi.bravintech.com/api/public/v1/trial-requests";

export default function TrialForm() {
  const [status, setStatus] = useState("");
  const [sending, setSending] = useState(false);

  useEffect(() => {
    const supplied = new URLSearchParams(window.location.search).get("server")?.trim();
    const input = document.querySelector<HTMLInputElement>("#serverInstallationId");
    if (input && supplied && /^[0-9a-f]{64}$/i.test(supplied)) input.value = supplied.toLowerCase();
  }, []);

  async function submit(event: SubmitEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = event.currentTarget;
    const data = new FormData(form);
    const website = String(data.get("website") || "");
    if (website) return;
    setSending(true);
    setStatus("Enviando sua solicitação…");
    try {
      const response = await fetch(endpoint, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          organizationName: data.get("organizationName"),
          contactName: data.get("contactName"),
          contactEmail: data.get("contactEmail"),
          contactPhone: data.get("contactPhone") || null,
          expectedDevices: Number(data.get("expectedDevices")),
          serverInstallationId: data.get("serverInstallationId") || null,
          privacyAccepted: data.get("privacyAccepted") === "on",
          website,
        }),
      });
      if (!response.ok) {
        const problem = await response.json().catch(() => null);
        throw new Error(problem?.message || "Não foi possível registrar a solicitação.");
      }
      form.reset();
      setStatus("Solicitação recebida. Nossa equipe fará a análise antes de liberar a avaliação.");
    } catch (error) {
      setStatus(error instanceof Error ? error.message : "Falha de comunicação. Tente novamente em alguns minutos.");
    } finally {
      setSending(false);
    }
  }

  return <form className="form" onSubmit={submit}>
    <div className="field-grid">
      <div className="field"><label htmlFor="organizationName">Instituição</label><input id="organizationName" name="organizationName" required maxLength={160} autoComplete="organization" /></div>
      <div className="field"><label htmlFor="contactName">Responsável</label><input id="contactName" name="contactName" required maxLength={120} autoComplete="name" /></div>
      <div className="field"><label htmlFor="contactEmail">E-mail profissional</label><input id="contactEmail" name="contactEmail" type="email" required maxLength={320} autoComplete="email" /></div>
      <div className="field"><label htmlFor="contactPhone">Telefone (opcional)</label><input id="contactPhone" name="contactPhone" type="tel" maxLength={40} autoComplete="tel" /></div>
      <div className="field"><label htmlFor="expectedDevices">Quantidade estimada de dispositivos</label><input id="expectedDevices" name="expectedDevices" type="number" required min={6} max={10000} defaultValue={15} /></div>
      <div className="field"><label htmlFor="serverInstallationId">ServerInstallationId (se já instalou)</label><input id="serverInstallationId" name="serverInstallationId" maxLength={64} pattern="[0-9a-fA-F]{64}" spellCheck={false} /></div>
    </div>
    <div className="field" aria-hidden="true" style={{position:"absolute", left:"-10000px"}}><label htmlFor="website">Website</label><input id="website" name="website" tabIndex={-1} autoComplete="off" /></div>
    <label className="field checkbox"><input type="checkbox" name="privacyAccepted" required /><span>Li o <a href="/privacidade/">Aviso de Privacidade</a> e autorizo o uso destes dados para análise e contato sobre a avaliação.</span></label>
    <button className="button" disabled={sending}>{sending ? "Enviando…" : "Enviar solicitação"}</button>
    <p className="form-status" role="status" aria-live="polite">{status}</p>
  </form>;
}
