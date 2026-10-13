import { CommonModule, CurrencyPipe, DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';

import { PaymentHistoryResponse, PaymentStatusResponse } from '../../models/transaction.models';
import { TransactionService } from '../../services/transaction.service';

@Component({
  selector: 'app-payment-history-page',
  standalone: true,
  imports: [CommonModule, CurrencyPipe, DatePipe],
  templateUrl: './payment-history-page.component.html',
  styleUrl: './payment-history-page.component.scss',
})
export class PaymentHistoryPageComponent {
  private readonly transactionService = inject(TransactionService);
  private readonly pageSize = 20;

  readonly history = signal<PaymentHistoryResponse | null>(null);
  readonly selectedPayment = signal<PaymentStatusResponse | null>(null);
  readonly isLoading = signal(true);
  readonly isLoadingDetail = signal(false);
  readonly error = signal<string | null>(null);
  readonly detailError = signal<string | null>(null);

  constructor() {
    this.loadPage(1);
  }

  loadPage(page: number): void {
    this.isLoading.set(true);
    this.error.set(null);
    this.selectedPayment.set(null);
    this.detailError.set(null);

    this.transactionService.getPaymentHistory(page, this.pageSize).subscribe({
      next: (history) => {
        this.history.set(history);
        this.isLoading.set(false);
      },
      error: (error: HttpErrorResponse) => {
        this.error.set(this.requestErrorMessage(error, 'Impossible de charger l’historique des paiements.'));
        this.isLoading.set(false);
      },
    });
  }

  loadPaymentDetails(paymentId: string): void {
    if (this.isLoadingDetail()) {
      return;
    }

    this.isLoadingDetail.set(true);
    this.detailError.set(null);
    this.selectedPayment.set(null);

    this.transactionService.getPaymentStatus(paymentId).subscribe({
      next: (payment) => {
        this.selectedPayment.set(payment);
        this.isLoadingDetail.set(false);
      },
      error: (error: HttpErrorResponse) => {
        this.detailError.set(this.requestErrorMessage(error, 'Le détail du paiement est temporairement inaccessible.'));
        this.isLoadingDetail.set(false);
      },
    });
  }

  previousPage(): void {
    const currentPage = this.history()?.page ?? 1;
    if (currentPage > 1) {
      this.loadPage(currentPage - 1);
    }
  }

  nextPage(): void {
    const history = this.history();
    if (history && history.page < history.totalPages) {
      this.loadPage(history.page + 1);
    }
  }

  paymentStatusLabel(payment: PaymentStatusResponse): string {
    if (payment.fraudAnalysisState === 'ANALYSIS_UNAVAILABLE') {
      return 'Analyse Fraud indisponible';
    }
    if (payment.fraudAnalysisState === 'TECHNICAL_ERROR') {
      return 'Erreur technique pendant l’analyse Fraud';
    }

    switch (payment.status) {
      case 'CREATED': return 'Demande créée';
      case 'FRAUD_CHECK_PENDING': return 'Analyse Fraud en cours';
      case 'APPROVED': return 'Fraud approuvé, exécution bancaire en attente';
      case 'MANUAL_REVIEW': return 'En attente de revue manuelle';
      case 'TRANSACTION_PENDING': return 'Exécution bancaire en attente';
      case 'COMPLETED': return 'Terminé après confirmation bancaire';
      case 'FAILED': return 'Échec confirmé';
      case 'BLOCKED': return 'Bloqué par Fraud';
      case 'EXPIRED': return 'Autorisation expirée';
      case 'CANCELLED': return 'Annulé';
      default: return `Statut : ${payment.status}`;
    }
  }

  private requestErrorMessage(error: HttpErrorResponse, fallback: string): string {
    if (error.status === 401) {
      return 'Votre session a expiré. Reconnectez-vous avant de consulter vos paiements.';
    }
    if (error.status === 0 || error.status >= 500) {
      return 'Le service Payment est temporairement indisponible. Aucun résultat financier n’est déduit.';
    }
    if (error.status === 404) {
      return 'Ce paiement n’est pas disponible avec la session actuelle.';
    }
    return fallback;
  }
}
