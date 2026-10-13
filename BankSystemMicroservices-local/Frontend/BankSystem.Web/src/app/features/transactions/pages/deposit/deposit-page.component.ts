import { Component } from '@angular/core';

import { TransactionFormComponent } from '../../components/transaction-form.component';

@Component({
  selector: 'app-deposit-page',
  standalone: true,
  imports: [TransactionFormComponent],
  template: '<app-transaction-form operation="deposit" title="Effectuer un dépôt" />',
})
export class DepositPageComponent {}
