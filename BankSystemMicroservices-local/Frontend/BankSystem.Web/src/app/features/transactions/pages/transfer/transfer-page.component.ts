import { Component } from '@angular/core';

import { TransactionFormComponent } from '../../components/transaction-form.component';

@Component({
  selector: 'app-transfer-page',
  standalone: true,
  imports: [TransactionFormComponent],
  template: '<app-transaction-form operation="transfer" title="Effectuer un virement" />',
})
export class TransferPageComponent {}
