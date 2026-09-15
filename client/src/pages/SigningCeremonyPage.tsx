import { useEffect, useRef, useState } from 'react';
import { useParams } from 'react-router-dom';
import axios from 'axios';
import apiClient from '../api/client';
import BrandMark from '../components/BrandMark';
import './ceremony.css';

type SignatureFieldKind = 'Signature' | 'Initial' | 'DateSigned' | 'Text' | 'Checkbox';

interface CeremonySignatureFieldDto {
  id: string;
  kind: SignatureFieldKind;
  pageNumber: number;
  x: number;
  y: number;
  width: number;
  height: number;
  value: string | null;
}

interface CeremonyRecipientDto {
  id: string;
  name: string;
  email: string;
  role: 'Signer' | 'Cc';
  status: 'Pending' | 'Sent' | 'Viewed' | 'Consented' | 'Signed' | 'Declined';
}

interface CeremonyContextDto {
  envelopeId: string;
  envelopeName: string;
  message: string | null;
  envelopeStatus: 'Draft' | 'Sent' | 'Completed' | 'Declined' | 'Voided';
  recipient: CeremonyRecipientDto;
  fields: CeremonySignatureFieldDto[];
}

interface CeremonyActionResultDto {
  recipientStatus: CeremonyRecipientDto['status'];
  envelopeStatus: CeremonyContextDto['envelopeStatus'];
  envelopeCompleted: boolean;
}

type Stage = 'loading' | 'invalid' | 'consent' | 'signing' | 'completed' | 'declined';

function SignaturePad({
  onChange,
  label,
}: {
  onChange: (dataUrl: string | null) => void;
  label: string;
}) {
  const canvasRef = useRef<HTMLCanvasElement | null>(null);
  const drawingRef = useRef(false);
  const hasInkRef = useRef(false);

  const getPos = (canvas: HTMLCanvasElement, e: React.PointerEvent<HTMLCanvasElement>) => {
    const rect = canvas.getBoundingClientRect();
    return { x: e.clientX - rect.left, y: e.clientY - rect.top };
  };

  const handlePointerDown = (e: React.PointerEvent<HTMLCanvasElement>) => {
    const canvas = canvasRef.current;
    if (!canvas) return;
    canvas.setPointerCapture(e.pointerId);
    const ctx = canvas.getContext('2d');
    if (!ctx) return;
    const { x, y } = getPos(canvas, e);
    ctx.beginPath();
    ctx.moveTo(x, y);
    drawingRef.current = true;
  };

  const handlePointerMove = (e: React.PointerEvent<HTMLCanvasElement>) => {
    if (!drawingRef.current) return;
    const canvas = canvasRef.current;
    if (!canvas) return;
    const ctx = canvas.getContext('2d');
    if (!ctx) return;
    const { x, y } = getPos(canvas, e);
    ctx.lineWidth = 2;
    ctx.lineCap = 'round';
    ctx.strokeStyle = '#1b1f2e';
    ctx.lineTo(x, y);
    ctx.stroke();
    hasInkRef.current = true;
  };

  const handlePointerUp = () => {
    if (!drawingRef.current) return;
    drawingRef.current = false;
    const canvas = canvasRef.current;
    if (canvas && hasInkRef.current) {
      onChange(canvas.toDataURL('image/png'));
    }
  };

  const handleClear = () => {
    const canvas = canvasRef.current;
    if (!canvas) return;
    const ctx = canvas.getContext('2d');
    ctx?.clearRect(0, 0, canvas.width, canvas.height);
    hasInkRef.current = false;
    onChange(null);
  };

  return (
    <div className="sig-pad">
      <canvas
        ref={canvasRef}
        width={360}
        height={130}
        className="sig-pad-canvas"
        aria-label={label}
        onPointerDown={handlePointerDown}
        onPointerMove={handlePointerMove}
        onPointerUp={handlePointerUp}
        onPointerLeave={handlePointerUp}
      />
      <button type="button" className="btn" onClick={handleClear}>
        Clear
      </button>
    </div>
  );
}

export default function SigningCeremonyPage() {
  const { token } = useParams<{ token: string }>();
  const [stage, setStage] = useState<Stage>('loading');
  const [context, setContext] = useState<CeremonyContextDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [consentChecked, setConsentChecked] = useState(false);
  const [fieldValues, setFieldValues] = useState<Record<string, string>>({});
  const [envelopeCompleted, setEnvelopeCompleted] = useState(false);
  const [declineOpen, setDeclineOpen] = useState(false);
  const [declineReason, setDeclineReason] = useState('');

  useEffect(() => {
    if (!token) return;
    let cancelled = false;
    (async () => {
      try {
        const response = await apiClient.get<CeremonyContextDto>(`/api/ceremony/${token}`);
        if (cancelled) return;
        setContext(response.data);
        const alreadyConsented =
          response.data.recipient.status === 'Consented' ||
          response.data.recipient.status === 'Signed';
        setStage(alreadyConsented ? 'signing' : 'consent');
      } catch {
        if (!cancelled) setStage('invalid');
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [token]);

  const handleConsent = async () => {
    if (!token || !consentChecked) return;
    setSubmitting(true);
    setError(null);
    try {
      await apiClient.post(`/api/ceremony/${token}/consent`);
      setStage('signing');
    } catch {
      setError('Could not record consent. Please try again.');
    } finally {
      setSubmitting(false);
    }
  };

  const setFieldValue = (fieldId: string, value: string) => {
    setFieldValues((prev) => ({ ...prev, [fieldId]: value }));
  };

  const handleSign = async () => {
    if (!token || !context) return;
    const missing = context.fields.filter(
      (f) => f.kind !== 'Checkbox' && !fieldValues[f.id]?.trim(),
    );
    if (missing.length > 0) {
      setError('Please complete every field before signing.');
      return;
    }
    setSubmitting(true);
    setError(null);
    try {
      const fields = context.fields.map((f) => {
        const raw = fieldValues[f.id] ?? '';
        const isImage = f.kind === 'Signature' || f.kind === 'Initial';
        return {
          fieldId: f.id,
          value: isImage ? 'signed' : raw,
          signatureImageBase64: isImage ? raw || null : null,
        };
      });
      const response = await apiClient.post<CeremonyActionResultDto>(
        `/api/ceremony/${token}/complete`,
        { fields },
      );
      setEnvelopeCompleted(response.data.envelopeCompleted);
      setStage('completed');
    } catch (err) {
      const message =
        axios.isAxiosError(err) && typeof err.response?.data?.error === 'string'
          ? err.response.data.error
          : 'Could not submit your signature. Please try again.';
      setError(message);
    } finally {
      setSubmitting(false);
    }
  };

  const handleDecline = async () => {
    if (!token) return;
    setSubmitting(true);
    setError(null);
    try {
      await apiClient.post(`/api/ceremony/${token}/decline`, { reason: declineReason || null });
      setDeclineOpen(false);
      setStage('declined');
    } catch {
      setError('Could not submit your decline. Please try again.');
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div className="ceremony-page">
      <header className="ceremony-header">
        <BrandMark size="sm" />
        <div className="ceremony-header-text">
          <div className="ceremony-brand">
            Nexus <span>Docs</span>
          </div>
          {context && <div className="ceremony-doc-name">{context.envelopeName}</div>}
        </div>
      </header>

      <main className="ceremony-main">
        {stage === 'loading' && (
          <div className="ceremony-card ceremony-center">
            <p className="ceremony-muted">Loading document…</p>
          </div>
        )}

        {stage === 'invalid' && (
          <div className="ceremony-card ceremony-center">
            <h1 className="ceremony-title">This link is no longer valid</h1>
            <p className="ceremony-muted">
              It may have expired, already been used, or the link may be incorrect. Please contact
              the sender to request a new signing link.
            </p>
          </div>
        )}

        {(stage === 'consent' || stage === 'signing') && context && (
          <div className="ceremony-layout">
            <section className="ceremony-card ceremony-doc-panel">
              <div className="ceremony-doc-panel-head">
                <h2 className="ceremony-subtitle">{context.envelopeName}</h2>
                {context.message && <p className="ceremony-muted">{context.message}</p>}
              </div>
              <iframe
                className="ceremony-doc-frame"
                src={`/api/ceremony/${token}/document`}
                title="Document to sign"
              />
            </section>

            <section className="ceremony-card ceremony-action-panel">
              {stage === 'consent' && (
                <>
                  <h1 className="ceremony-title">Review &amp; consent</h1>
                  <p className="ceremony-muted">
                    You've been asked to sign <strong>{context.envelopeName}</strong>. Review the
                    document on the left, then consent to sign electronically to continue.
                  </p>
                  <label className="ceremony-checkbox-row">
                    <input
                      type="checkbox"
                      checked={consentChecked}
                      onChange={(e) => setConsentChecked(e.target.checked)}
                    />
                    <span>I agree to sign this document electronically.</span>
                  </label>
                  {error && <p className="ceremony-error">{error}</p>}
                  <button
                    type="button"
                    className="btn primary"
                    disabled={!consentChecked || submitting}
                    onClick={handleConsent}
                  >
                    {submitting ? 'Continuing…' : 'Continue'}
                  </button>
                </>
              )}

              {stage === 'signing' && (
                <>
                  <h1 className="ceremony-title">Complete your fields</h1>
                  <p className="ceremony-muted">
                    Fill in each field below, then sign to finish.
                  </p>

                  <div className="ceremony-fields">
                    {context.fields.map((field) => (
                      <div key={field.id} className="ceremony-field">
                        <label className="field-label">
                          {field.kind}
                          {field.kind === 'Checkbox' ? '' : ' *'}
                        </label>

                        {(field.kind === 'Signature' || field.kind === 'Initial') && (
                          <SignaturePad
                            label={field.kind}
                            onChange={(dataUrl) => setFieldValue(field.id, dataUrl ?? '')}
                          />
                        )}

                        {field.kind === 'Text' && (
                          <input
                            type="text"
                            className="field-input"
                            value={fieldValues[field.id] ?? ''}
                            onChange={(e) => setFieldValue(field.id, e.target.value)}
                          />
                        )}

                        {field.kind === 'DateSigned' && (
                          <input
                            type="date"
                            className="field-input"
                            value={fieldValues[field.id] ?? ''}
                            onChange={(e) => setFieldValue(field.id, e.target.value)}
                          />
                        )}

                        {field.kind === 'Checkbox' && (
                          <label className="ceremony-checkbox-row">
                            <input
                              type="checkbox"
                              checked={fieldValues[field.id] === 'true'}
                              onChange={(e) =>
                                setFieldValue(field.id, e.target.checked ? 'true' : 'false')
                              }
                            />
                            <span>Check to confirm</span>
                          </label>
                        )}
                      </div>
                    ))}
                  </div>

                  {error && <p className="ceremony-error">{error}</p>}

                  <div className="ceremony-action-row">
                    <button
                      type="button"
                      className="btn primary"
                      disabled={submitting}
                      onClick={handleSign}
                    >
                      {submitting ? 'Signing…' : 'Sign'}
                    </button>
                    <button
                      type="button"
                      className="ceremony-decline-link"
                      onClick={() => setDeclineOpen(true)}
                    >
                      Decline to sign
                    </button>
                  </div>
                </>
              )}
            </section>
          </div>
        )}

        {stage === 'completed' && (
          <div className="ceremony-card ceremony-center">
            <h1 className="ceremony-title">
              {envelopeCompleted ? 'This document is now fully executed' : "You've signed this document"}
            </h1>
            <p className="ceremony-muted">
              {envelopeCompleted
                ? 'All recipients have signed. A copy will be available from the sender.'
                : "Thanks — your part is complete. You'll be notified once everyone has signed."}
            </p>
          </div>
        )}

        {stage === 'declined' && (
          <div className="ceremony-card ceremony-center">
            <h1 className="ceremony-title">You've declined to sign</h1>
            <p className="ceremony-muted">The sender has been notified of your decision.</p>
          </div>
        )}
      </main>

      {declineOpen && (
        <div className="ceremony-modal-overlay">
          <div className="ceremony-modal panel">
            <h2 className="ceremony-subtitle">Decline to sign</h2>
            <p className="ceremony-muted">Let the sender know why (optional).</p>
            <textarea
              className="field-input ceremony-decline-textarea"
              rows={4}
              value={declineReason}
              onChange={(e) => setDeclineReason(e.target.value)}
              placeholder="Reason for declining…"
            />
            {error && <p className="ceremony-error">{error}</p>}
            <div className="ceremony-action-row">
              <button
                type="button"
                className="btn danger"
                disabled={submitting}
                onClick={handleDecline}
              >
                {submitting ? 'Submitting…' : 'Confirm decline'}
              </button>
              <button type="button" className="btn" onClick={() => setDeclineOpen(false)}>
                Cancel
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
