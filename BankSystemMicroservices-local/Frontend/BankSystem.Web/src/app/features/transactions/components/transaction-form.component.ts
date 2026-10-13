import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, Input, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { finalize } from 'rxjs';

import { AuthService } from '../../../core/auth/auth.service';
import { Account } from '../../accounts/models/account.models';
import { AccountService } from '../../accounts/services/account.service';
import { PaymentStatusResponse, PaymentTransferRequest } from '../models/transaction.models';
import { TransactionOperation, TransactionService } from '../services/transaction.service';

interface TransactionFormValue {
  accountId: string;
  toAccountId: string;
  amount: number;
  currency: string;
  description: string;
  reference: string;
}

interface TransferAttempt {
  request: PaymentTransferRequest;
  idempotencyKey: string;
}

interface PaymentRecoveryState {
  idempotencyKey: string;
  authSessionId: string;
  paymentId?: string;
}

@Component({
  selector: 'app-transaction-form',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  templateUrl: './transaction-form.component.html',
  styleUrl: './transaction-form.component.scss',
})
export class TransactionFormComponent implements OnInit {
  @Input({ required: true }) operation!: TransactionOperation;
  @Input({ required: true }) title!: string;

  private readonly formBuilder = inject(FormBuilder);
  private readonly authService = inject(AuthService);
  private readonly accountService = inject(AccountService);
  private readonly transactionService = inject(TransactionService);

  readonly accounts = signal<Account[]>([]);
  readonly isLoadingAccounts = signal(true);
  readonly isSubmitting = signal(false);
  readonly isRefreshingPayment = signal(false);
  readonly error = signal<string | null>(null);
  readonly success = signal<string | null>(null);
  readonly payment = signal<PaymentStatusResponse | null>(null);
  readonly recoveryPaymentId = signal<string | null>(null);
  readonly paymentLookupBlocked = signal(false);
  readonly recoverySessionChanged = signal(false);
  readonly recoveryStorageAvailable = signal(true);
  readonly requiresIdempotencyLookup = signal(false);
  readonly isSearchingRecovery = signal(false);
  readonly requiresSameRequestRetry = signal(false);
  readonly transferAttempt = signal<TransferAttempt | null>(null);
  private readonly recoveryStorageKey = 'bank-system.payment-recovery';
  private recoveryIdempotencyKey: string | null = null;
  private recoveryAuthSessionId: string | null = null;
  readonly form = this.formBuilder.nonNullable.group({
    accountId: ['', Validators.required],
    toAccountId: [''],
    amount: [0, [Validators.required, Validators.min(0.01)]],
    currency: ['USD', Validators.required],
    description: ['', Validators.maxLength(250)],
    reference: ['', Validators.maxLength(100)],
  });

  get isTransfer(): boolean {
    return this.operation === 'transfer';
  }

  constructor() {
    this.accountService.getMyAccounts().subscribe({
      next: (accounts) => {
        this.accounts.set(accounts);
        this.isLoadingAccounts.set(false);
      },
      error: () => {
        this.error.set('Impossible de charger vos comptes.');
        this.isLoadingAccounts.set(false);
      },
    });
  }

  ngOnInit(): void {
    if (this.isTransfer) {
      this.restorePaymentRecovery();
    }
  }

  submit(): void {
    if (this.isSubmitting() || this.payment() || this.recoveryPaymentId()
      || this.recoverySessionChanged() || this.requiresIdempotencyLookup()) {
      return;
    }

    if (this.isTransfer) {
      this.form.controls.toAccountId.addValidators(Validators.required);
      this.form.controls.toAccountId.updateValueAndValidity({ emitEvent: false });
    }

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const value = this.form.getRawValue();
    if (this.isTransfer && value.accountId === value.toAccountId) {
      this.error.set('Le compte émetteur et le compte destinataire doivent être différents.');
      return;
    }
    if (this.isTransfer && !this.recoveryStorageAvailable()) {
      this.error.set('Le stockage de reprise est indisponible. Le virement n’a pas été envoyé.');
      return;
    }

    this.error.set(null);
    this.success.set(null);
    this.isSubmitting.set(true);

    if (this.isTransfer) {
      this.submitTransfer(value);
      return;
    }

    this.transactionService.create(this.operation, {
      ...value,
      toAccountId: null,
    }).pipe(finalize(() => this.isSubmitting.set(false))).subscribe({
      next: (transaction) => this.success.set(`Opération ${transaction.status.toLowerCase()} : ${transaction.reference || transaction.id}`),
      error: (error) => this.error.set(error.error?.error ?? error.error?.detail ?? 'Impossible d’enregistrer l’opération.'),
    });
  }

  paymentStatusLabel(payment: PaymentStatusResponse): string {
    if (payment.fraudAnalysisState === 'ANALYSIS_UNAVAILABLE') {
      return 'L’analyse Fraud n’a pas pu être réalisée. Aucun statut bancaire final n’est déduit.';
    }
    if (payment.fraudAnalysisState === 'TECHNICAL_ERROR') {
      return 'Une erreur technique a interrompu l’analyse Fraud. Le résultat reste à vérifier.';
    }

    switch (payment.status) {
      case 'CREATED':
        return 'Demande créée. Vérification de l’autorisation bancaire en cours.';
      case 'FRAUD_CHECK_PENDING':
        return 'Analyse Fraud en cours.';
      case 'APPROVED':
        return 'Analyse Fraud approuvée. Le mouvement bancaire reste en attente.';
      case 'MANUAL_REVIEW':
        return 'Le virement attend une revue manuelle.';
      case 'TRANSACTION_PENDING':
        return 'Le virement attend la confirmation du traitement bancaire.';
      case 'COMPLETED':
        return 'Virement terminé après confirmation bancaire.';
      case 'FAILED':
        return 'Le virement a été définitivement refusé.';
      case 'BLOCKED':
        return 'Le virement a été bloqué par l’analyse Fraud.';
      case 'EXPIRED':
        return 'L’autorisation bancaire a expiré. Le virement n’a pas été exécuté.';
      case 'CANCELLED':
        return 'Le virement a été annulé.';
      default:
        return 'État du virement : suivi requis.';
    }
  }

  refreshPaymentStatus(): void {
    const paymentId = this.payment()?.paymentId ?? this.recoveryPaymentId();
    if (!paymentId || this.isRefreshingPayment()) {
      return;
    }

    this.error.set(null);
    this.loadPaymentStatus(paymentId);
  }

  searchPaymentRecovery(): void {
    const idempotencyKey = this.recoveryIdempotencyKey;
    if (!idempotencyKey || !this.requiresIdempotencyLookup() || this.isSearchingRecovery()) {
      return;
    }

    this.isSearchingRecovery.set(true);
    this.error.set(null);
    this.transactionService.findPaymentByIdempotencyKey(idempotencyKey)
      .pipe(finalize(() => this.isSearchingRecovery.set(false)))
      .subscribe({
        next: (payment) => {
          this.payment.set(payment);
          this.recoveryPaymentId.set(payment.paymentId);
          this.requiresIdempotencyLookup.set(false);
          this.paymentLookupBlocked.set(false);
          this.recoverySessionChanged.set(false);
          this.requiresSameRequestRetry.set(false);
          this.savePaymentRecovery(payment.paymentId);
          this.error.set(null);
        },
        error: (error: HttpErrorResponse) => {
          if (error.status === 404) {
            this.requiresIdempotencyLookup.set(false);
            let sameAuthSession = false;
            try {
              sameAuthSession = this.recoveryAuthSessionId !== null
                && this.recoveryAuthSessionId === this.authService.getSessionId();
            } catch {
              sameAuthSession = false;
            }
            if (!sameAuthSession) {
              this.recoverySessionChanged.set(true);
            }
            if (sameAuthSession && !this.recoverySessionChanged()) {
              this.requiresSameRequestRetry.set(true);
              this.error.set('Aucune demande n’a été retrouvée. Aucun nouveau virement n’a été envoyé. Vous pouvez choisir de réessayer avec la même clé et exactement les mêmes données.');
            } else {
              this.error.set('Aucune demande accessible n’a été retrouvée pour cette session. Aucun nouveau virement n’a été envoyé.');
            }
          } else {
            this.requiresIdempotencyLookup.set(true);
            if (error.status === 401) {
              this.error.set('La session a expiré. Reconnectez-vous, puis relancez la consultation de la reprise.');
            } else {
              this.error.set('La recherche du paiement est temporairement indisponible. Aucun nouveau virement n’a été envoyé.');
            }
          }
        },
      });
  }

  private loadPaymentStatus(paymentId: string): void {
    if (this.isRefreshingPayment()) {
      return;
    }

    this.isRefreshingPayment.set(true);
    this.transactionService.getPaymentStatus(paymentId)
      .pipe(finalize(() => this.isRefreshingPayment.set(false)))
      .subscribe({
        next: (payment) => {
          this.payment.set(payment);
          this.recoveryPaymentId.set(payment.paymentId);
          this.paymentLookupBlocked.set(false);
          this.recoverySessionChanged.set(false);
          this.requiresSameRequestRetry.set(false);
          this.savePaymentRecovery(payment.paymentId);
        },
        error: (error: HttpErrorResponse) => {
          if (error.status === 404) {
            this.paymentLookupBlocked.set(true);
            this.error.set('Cette demande est introuvable ou inaccessible avec la session actuelle. Ne créez pas une nouvelle demande avant vérification.');
          } else if (error.status === 401) {
            this.error.set('La session a expiré. Reconnectez-vous avant de consulter le statut.');
          } else {
            this.error.set('Le statut est temporairement inaccessible. Aucun résultat financier n’est déduit.');
          }
        },
      });
  }

  startNewTransfer(): void {
    const currentPayment = this.payment();
    if (currentPayment && !this.isPaymentTerminal(currentPayment.status)) {
      return;
    }

    this.payment.set(null);
    this.paymentLookupBlocked.set(false);
    this.clearPaymentRecovery();
    this.error.set(null);
    this.success.set(null);
    this.form.reset({
      accountId: '',
      toAccountId: '',
      amount: 0,
      currency: 'USD',
      description: '',
      reference: '',
    });
  }

  private submitTransfer(value: TransactionFormValue): void {
    const existingAttempt = this.transferAttempt();
    const attempt = existingAttempt ?? {
      request: this.createTransferRequest(value),
      idempotencyKey: this.recoveryIdempotencyKey ?? crypto.randomUUID(),
    };
    this.transferAttempt.set(attempt);
    this.recoveryIdempotencyKey = attempt.idempotencyKey;
    this.requiresSameRequestRetry.set(false);
    if (!this.savePaymentRecovery()) {
      this.isSubmitting.set(false);
      return;
    }

    this.transactionService.createTransfer(attempt.request, attempt.idempotencyKey)
      .pipe(finalize(() => this.isSubmitting.set(false)))
      .subscribe({
        next: (payment) => {
          this.payment.set(payment);
          this.recoveryPaymentId.set(payment.paymentId);
          this.savePaymentRecovery(payment.paymentId);
          this.error.set(null);
        },
        error: (error) => {
          const paymentId = error.error?.paymentId;
          if (typeof paymentId === 'string' && paymentId.length > 0) {
            this.recoveryPaymentId.set(paymentId);
            this.savePaymentRecovery(paymentId);
            this.loadPaymentStatus(paymentId);
          } else if (error.status === 400) {
            this.transferAttempt.set(null);
          } else {
            this.requiresIdempotencyLookup.set(true);
          }
          this.error.set(this.transferErrorMessage(error));
        },
      });
  }

  private restorePaymentRecovery(): void {
    let storedValue: string | null;
    try {
      storedValue = sessionStorage.getItem(this.recoveryStorageKey);
    } catch {
      this.recoveryStorageAvailable.set(false);
      this.error.set('Le stockage de reprise est indisponible. Aucun nouveau virement ne sera envoyé.');
      return;
    }
    if (!storedValue) {
      return;
    }

    try {
      const state = JSON.parse(storedValue) as PaymentRecoveryState;
      if (typeof state.idempotencyKey !== 'string'
        || !state.idempotencyKey
        || state.idempotencyKey.length > 255
        || typeof state.authSessionId !== 'string'
        || (state.paymentId !== undefined && typeof state.paymentId !== 'string')) {
        this.recoveryStorageAvailable.set(false);
        this.error.set('Les informations de reprise sont invalides. Aucun nouveau virement ne sera envoyé.');
        return;
      }

      this.recoveryIdempotencyKey = state.idempotencyKey;
      this.recoveryAuthSessionId = state.authSessionId;
      if (state.authSessionId !== this.authService.getSessionId()) {
        this.recoverySessionChanged.set(true);
        if (state.paymentId) {
          this.recoveryPaymentId.set(state.paymentId);
          this.loadPaymentStatus(state.paymentId);
        } else {
          this.requiresIdempotencyLookup.set(true);
          this.error.set('Une demande précédente doit être recherchée avec la nouvelle session. Aucun nouveau virement ne sera envoyé.');
        }
        return;
      }

      if (state.paymentId) {
        this.recoveryPaymentId.set(state.paymentId);
        this.loadPaymentStatus(state.paymentId);
      } else {
        this.requiresIdempotencyLookup.set(true);
        this.error.set('Recherchez d’abord la demande avec sa clé de reprise. Aucun nouveau virement ne sera envoyé automatiquement.');
      }
    } catch {
      this.recoveryStorageAvailable.set(false);
      this.error.set('Les informations de reprise ne peuvent pas être lues. Aucun nouveau virement ne sera envoyé.');
    }
  }

  private savePaymentRecovery(paymentId?: string): boolean {
    const idempotencyKey = this.recoveryIdempotencyKey;
    if (!idempotencyKey) {
      return false;
    }

    try {
      const authSessionId = this.authService.getSessionId();
      const state: PaymentRecoveryState = paymentId
        ? { idempotencyKey, authSessionId, paymentId }
        : { idempotencyKey, authSessionId };
      sessionStorage.setItem(this.recoveryStorageKey, JSON.stringify(state));
      this.recoveryAuthSessionId = authSessionId;
      return true;
    } catch {
      this.recoveryStorageAvailable.set(false);
      this.error.set('La reprise après rechargement n’est pas disponible dans ce navigateur.');
      return false;
    }
  }

  private clearPaymentRecovery(): void {
    this.recoveryIdempotencyKey = null;
    this.recoveryAuthSessionId = null;
    this.recoveryPaymentId.set(null);
    this.requiresSameRequestRetry.set(false);
    this.requiresIdempotencyLookup.set(false);
    this.transferAttempt.set(null);
    this.paymentLookupBlocked.set(false);
    this.recoverySessionChanged.set(false);
    try {
      sessionStorage.removeItem(this.recoveryStorageKey);
    } catch {
      this.recoveryStorageAvailable.set(false);
      this.error.set('Impossible d’effacer les informations de reprise dans ce navigateur.');
    }
  }

  canStartNewTransfer(payment: PaymentStatusResponse): boolean {
    return payment.fraudAnalysisState !== 'ANALYSIS_UNAVAILABLE'
      && payment.fraudAnalysisState !== 'TECHNICAL_ERROR'
      && this.isPaymentTerminal(payment.status);
  }

  private isPaymentTerminal(status: string): boolean {
    return ['COMPLETED', 'FAILED', 'BLOCKED', 'EXPIRED', 'CANCELLED'].includes(status);
  }

  private createTransferRequest(value: TransactionFormValue): PaymentTransferRequest {
    const merchantReference = value.reference.trim();
    return {
      operationType: 'BANK_TRANSFER',
      sourceAccountId: value.accountId,
      beneficiaryAccountId: value.toAccountId,
      amount: value.amount,
      currency: value.currency,
      ...(merchantReference ? { merchantReference } : {}),
    };
  }

  private transferErrorMessage(error: HttpErrorResponse): string {
    const code = String(error.error?.code ?? error.error?.detail ?? '');
    if (code.includes('BANK_TRANSFER_FRAUD_DISABLED')) {
      return 'Le parcours de virement est désactivé. Aucun mouvement n’est confirmé.';
    }
    if (code.includes('ACCOUNT_AUTHORIZATION_DENIED')) {
      return 'Account a refusé l’autorisation. Consultez le statut avant toute nouvelle demande.';
    }
    if (error.status === 401) {
      return 'La session a expiré. Reconnectez-vous, puis consultez ou reprenez la même demande.';
    }
    if (error.status === 409) {
      return 'Cette clé est associée à une demande différente ou à une autorisation déjà traitée. Aucun nouveau virement ne doit être tenté avec une autre clé avant vérification.';
    }
    if (error.status === 400) {
      return 'La demande est invalide. Corrigez les données indiquées et conservez la même clé de reprise.';
    }
    if (error.status === 0 || error.status >= 500) {
      return 'Le résultat de la demande est incertain. Consultez le statut ou réessayez exactement la même demande avec la même clé.';
    }
    return 'Le résultat de la demande est incertain. Réessayez la même demande pour éviter un doublon.';
  }

  hasError(controlName: 'accountId' | 'toAccountId' | 'amount', errorName: string): boolean {
    const control = this.form.controls[controlName];
    return control.touched && control.hasError(errorName);
  }

  formatIban(accountNumber: string): string {
    return accountNumber.replace(/(.{4})/g, '$1 ').trim();
  }
}
